// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The Print tab's only door to the printer: Moonraker's HTTP API, read-only except for ONE file
// upload. SAFETY CONTRACT (enforced by tests/PhiMech.SolidWorks.Tests/PrintSafetyTests.cs, which
// reads this folder's source):
//
//   * Nothing here starts, pauses, resumes or cancels a print, sets a temperature, homes or moves
//     an axis, or runs G-code or a macro. The only POST is /server/files/upload, which stores a
//     file in the printer's gcodes root with print=false sent explicitly (Moonraker's default is
//     also false; being explicit means no future default can turn an upload into a job).
//   * An existing file is never overwritten: before uploading, the target folder is listed and a
//     file of the same name is fetched and hashed; the upload is refused unless it is the same
//     bytes (then nothing is sent at all).
//   * Starting a print belongs to the printer's own screen or to the separate configured printer server,
//     behind its two-step confirm token, where the interlocks and the audit log live. This is the
//     same line PhiMech's phimech/print/printer.py draws.
//
// Proxies are bypassed: the printer is a LAN host, and an HTTP proxy answers for LAN addresses
// with errors that look like the printer's. Timeouts are short, and every network failure is
// turned into a PrinterException whose message says what to do (your configured local network).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PhiMech.SolidWorks.Print
{
    public enum PrinterProblem
    {
        Unreachable,
        Timeout,
        Moonraker,
        KlippyNotReady,
        TooLarge,
        NameCollision,
        BadResponse,
    }

    public sealed class PrinterException : Exception
    {
        public PrinterException(PrinterProblem problem, string message, Exception inner = null, int? httpStatus = null)
            : base(message, inner)
        {
            Problem = problem;
            HttpStatus = httpStatus;
        }

        public PrinterProblem Problem { get; }

        /// <summary>The HTTP status when the printer answered with an error (null otherwise).</summary>
        public int? HttpStatus { get; }
    }

    /// <summary>Everything "Check printer" learned. Status/Config may be null when Klipper is not ready.</summary>
    public sealed class PrinterCheck
    {
        public string Url;
        public PrinterInfo Info;
        public PrinterStatus Status;
        public MachineConfig Config;
        public BuildVolume Envelope;
        /// <summary>A part of the check that failed after /printer/info answered (null when none).</summary>
        public string Warning;
    }

    public sealed class UploadOutcome
    {
        /// <summary>gcodes-relative path on the printer, e.g. phimech/bracket-petg-1a2b3c4d.gcode.3mf.</summary>
        public string RemotePath;
        public long SizeBytes;
        /// <summary>True when the same bytes were already there, so nothing was sent.</summary>
        public bool AlreadyThere;
        /// <summary>What Moonraker reported; always false unless the printer misbehaves.</summary>
        public bool PrintStarted;
        /// <summary>The printer was busy with a job when the file was stored (a note, not a refusal).</summary>
        public bool PrinterWasBusy;
    }

    public sealed class MoonrakerClient : IDisposable
    {
        public const string StatusQuery = "/printer/objects/query?print_stats&display_status&heater_bed&extruder&toolhead&webhooks";
        public const string ConfigQuery = "/printer/objects/query?configfile=settings";
        public const string UploadPath = "/server/files/upload";
        public const string GcodeRoot = "gcodes";
        public const string Folder = "phimech";
        /// <summary>Moonraker's default [server] max_upload_size (1024 MiB).</summary>
        public const long MaxUploadBytes = 1024L * 1024 * 1024;

        /// <summary>Every read-only GET: short, so an off-network PC hears about it in seconds.</summary>
        public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan ReadTimeout;
        private readonly HttpClient _http;
        private readonly Uri _base;
        private readonly string _host;

        /// <param name="baseUrl">a URL PrinterUrl.Normalize accepted</param>
        /// <param name="handler">the transport (tests pass a fake; null = the network, proxies off)</param>
        /// <param name="readTimeout">per GET (null: DefaultReadTimeout)</param>
        public MoonrakerClient(string baseUrl, HttpMessageHandler handler = null, TimeSpan? readTimeout = null)
        {
            ReadTimeout = readTimeout ?? DefaultReadTimeout;
            if (!PrinterUrl.Normalize(baseUrl, out var url, out var why)) throw new ArgumentException(why, nameof(baseUrl));
            _base = new Uri(url);
            _host = _base.Host + ":" + _base.Port.ToString(CultureInfo.InvariantCulture);
            _http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan,   // per request, below
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PhiMech.SolidWorks/"
                + typeof(MoonrakerClient).Assembly.GetName().Version.ToString(3));
        }

        public string Url => _base.GetLeftPart(UriPartial.Authority);

        public void Dispose() => _http.Dispose();

        // ---- read-only -------------------------------------------------------------------------

        public async Task<PrinterInfo> InfoAsync() => Moonraker.ParseInfo(await GetTextAsync("/printer/info").ConfigureAwait(false));

        public async Task<PrinterStatus> StatusAsync() => Moonraker.ParseStatus(await GetTextAsync(StatusQuery).ConfigureAwait(false));

        public async Task<MachineConfig> ConfigAsync() => Moonraker.ParseConfig(await GetTextAsync(ConfigQuery).ConfigureAwait(false));

        /// <summary>The phimech/ folder in gcodes; empty when it does not exist yet (404).</summary>
        public async Task<RemoteDirectory> FolderAsync()
        {
            try
            {
                return Moonraker.ParseDirectory(await GetTextAsync("/server/files/directory?path=" + GcodeRoot + "/" + Folder).ConfigureAwait(false));
            }
            catch (PrinterException ex) when (ex.HttpStatus == 404)
            {
                return new RemoteDirectory();
            }
        }

        /// <summary>
        /// /printer/info, then the status objects, then the config. An unreachable printer throws;
        /// a Klipper that is not ready returns what it could read, with a Warning.
        /// </summary>
        public async Task<PrinterCheck> CheckAsync()
        {
            var check = new PrinterCheck { Url = Url, Info = await InfoAsync().ConfigureAwait(false) };
            try
            {
                check.Status = await StatusAsync().ConfigureAwait(false);
                check.Config = await ConfigAsync().ConfigureAwait(false);
            }
            catch (PrinterException ex) when (ex.Problem == PrinterProblem.Moonraker || ex.Problem == PrinterProblem.BadResponse
                                               || ex.Problem == PrinterProblem.KlippyNotReady)
            {
                check.Warning = check.Info.Ready ? ex.Message : KlippyText(check.Info);
            }
            check.Envelope = Moonraker.Envelope(check.Config, check.Status);
            return check;
        }

        public static string KlippyText(PrinterInfo info) =>
            "Klipper is not ready (" + (info?.KlippyState ?? "unknown") + ")"
            + (string.IsNullOrWhiteSpace(info?.StateMessage) ? "" : ": " + info.StateMessage.Trim())
            + ". The printer's own screen shows why; PhiMech does not restart or reset it.";

        // ---- the one write ------------------------------------------------------------------------

        /// <summary>
        /// Store <paramref name="localPath"/> as phimech/<paramref name="remoteName"/> in the printer's
        /// gcodes root. Never starts a print, never overwrites: a same-named file is refused unless
        /// its bytes hash to the same SHA-256, in which case nothing is sent.
        /// </summary>
        public async Task<UploadOutcome> UploadAsync(string localPath, string remoteName, bool printerBusy = false)
        {
            if (!PrintNaming.IsSafeRemoteName(remoteName))
                throw new ArgumentException("not a safe file name for the printer: " + remoteName, nameof(remoteName));
            var bytes = File.ReadAllBytes(localPath);
            var sha = Sha256Hex(bytes);
            var outcome = new UploadOutcome { RemotePath = Folder + "/" + remoteName, SizeBytes = bytes.LongLength, PrinterWasBusy = printerBusy };
            if (bytes.LongLength > MaxUploadBytes)
                throw new PrinterException(PrinterProblem.TooLarge, TooLargeText(bytes.LongLength, MaxUploadBytes, "Moonraker's upload limit"));

            var folder = await FolderAsync().ConfigureAwait(false);
            var existing = folder.Find(remoteName);
            if (existing != null)
            {
                if (existing.Size == bytes.LongLength)
                {
                    var theirs = await GetBytesAsync("/server/files/" + GcodeRoot + "/" + Folder + "/" + Uri.EscapeDataString(remoteName),
                        TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    if (Sha256Hex(theirs) == sha)
                    {
                        outcome.AlreadyThere = true;
                        return outcome;
                    }
                }
                throw new PrinterException(PrinterProblem.NameCollision,
                    "A different file named " + outcome.RemotePath + " is already on the printer, and PhiMech never overwrites a file "
                    + "there. Nothing was uploaded. Slice again (the name carries the content hash) or remove the old file on the printer.");
            }
            if (folder.FreeBytes != null && bytes.LongLength > folder.FreeBytes.Value)
                throw new PrinterException(PrinterProblem.TooLarge, TooLargeText(bytes.LongLength, folder.FreeBytes.Value, "the printer's free space"));

            using (var form = new MultipartFormDataContent("phimech" + Guid.NewGuid().ToString("N")))
            {
                form.Add(Field("root", GcodeRoot));
                form.Add(Field("path", Folder));
                // Explicitly NOT a print: the file is stored for a person to pick on the printer.
                form.Add(Field("print", "false"));
                // Moonraker compares this with its own hash of what arrived (HTTP 422 on a mismatch).
                form.Add(Field("checksum", sha));
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = Quoted("file"),
                    FileName = Quoted(remoteName),
                };
                file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file);

                var timeout = TimeSpan.FromSeconds(Math.Max(30, 15 + bytes.LongLength / (256 * 1024)));
                var text = await SendAsync(HttpMethod.Post, UploadPath, form, timeout).ConfigureAwait(false);
                try
                {
                    var r = JToken.Parse(text)["result"] ?? JToken.Parse(text);
                    var item = r["item"] as JObject;
                    var path = (string)item?["path"];
                    if (!string.IsNullOrEmpty(path)) outcome.RemotePath = path;
                    outcome.PrintStarted = r["print_started"]?.Type == JTokenType.Boolean && (bool)r["print_started"];
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Stored (HTTP 2xx) but the answer was not JSON: keep the path we asked for.
                }
            }
            return outcome;
        }

        /// <summary>A form field shaped like PhiMech's hand-built body: quoted name, no content type.</summary>
        private static HttpContent Field(string name, string value)
        {
            var c = new ByteArrayContent(Encoding.UTF8.GetBytes(value));
            c.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quoted(name) };
            return c;
        }

        private static string Quoted(string s) => "\"" + s + "\"";

        public static string TooLargeText(long size, long limit, string what) =>
            "The file is " + PrintFormat.Bytes(size) + ", more than " + what + " (" + PrintFormat.Bytes(limit) + "). Nothing was uploaded.";

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ---- transport -----------------------------------------------------------------------------

        private Task<string> GetTextAsync(string pathAndQuery) => SendAsync(HttpMethod.Get, pathAndQuery, null, ReadTimeout);

        private async Task<byte[]> GetBytesAsync(string pathAndQuery, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource(timeout))
            using (var req = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, pathAndQuery)))
            {
                try
                {
                    using (var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) throw HttpProblem(resp.StatusCode, Encoding.UTF8.GetString(body));
                        return body;
                    }
                }
                catch (Exception ex) when (!(ex is PrinterException))
                {
                    throw NetworkProblem(ex, timeout);
                }
            }
        }

        private async Task<string> SendAsync(HttpMethod method, string pathAndQuery, HttpContent content, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource(timeout))
            using (var req = new HttpRequestMessage(method, new Uri(_base, pathAndQuery)) { Content = content })
            {
                req.Headers.Accept.ParseAdd("application/json");
                try
                {
                    using (var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false))
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) throw HttpProblem(resp.StatusCode, text);
                        return text;
                    }
                }
                catch (Exception ex) when (!(ex is PrinterException))
                {
                    throw NetworkProblem(ex, timeout);
                }
            }
        }

        private PrinterException HttpProblem(HttpStatusCode code, string body)
        {
            var err = Moonraker.ParseError(body);
            var n = (int)code;
            var detail = err?.Message ?? (string.IsNullOrWhiteSpace(body) ? code.ToString() : Shorten(body));
            if (n == 413)
                return new PrinterException(PrinterProblem.TooLarge,
                    "The printer refused the file as too large (HTTP 413: " + detail + "). Nothing was uploaded.", null, n);
            if (n == 503 || (detail.IndexOf("klippy", StringComparison.OrdinalIgnoreCase) >= 0
                             && detail.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0))
                return new PrinterException(PrinterProblem.KlippyNotReady,
                    "The printer answered, but Klipper is not connected (HTTP " + n + ": " + detail + "). Check the printer's screen.", null, n);
            return new PrinterException(PrinterProblem.Moonraker, "The printer at " + _host + " answered HTTP " + n + ": " + detail, null, n);
        }

        private PrinterException NetworkProblem(Exception ex, TimeSpan timeout)
        {
            if (ex is FormatException)
                return new PrinterException(PrinterProblem.BadResponse, "The printer at " + _host + " sent an answer PhiMech could not read: " + ex.Message, ex);
            var network = "Check the configured printer address and local network, then click Check printer again.";
            if (ex is OperationCanceledException)
                return new PrinterException(PrinterProblem.Timeout,
                    "The printer at " + _host + " did not answer within " + timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)
                    + " s. " + network, ex);
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            return new PrinterException(PrinterProblem.Unreachable,
                "Could not reach the printer at " + _host + " (" + root.Message.Trim().TrimEnd('.') + "). " + network, ex);
        }

        private static string Shorten(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }
    }

    public static class PrintFormat
    {
        public static string Bytes(long n)
        {
            if (n < 1024) return n.ToString(CultureInfo.InvariantCulture) + " bytes";
            if (n < 1024L * 1024) return (n / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            if (n < 1024L * 1024 * 1024) return (n / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (n / 1073741824.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>1688 -> "28m 8s"; 78455 -> "21h 47m".</summary>
        public static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
            if (t.TotalHours >= 1) return ((int)t.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + t.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            if (t.TotalMinutes >= 1) return t.Minutes.ToString(CultureInfo.InvariantCulture) + "m " + t.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
            return t.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }
    }
}
