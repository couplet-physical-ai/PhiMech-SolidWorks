// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The Print tab's steps without the tab: check the printer, check the fit, slice an exported STL
// through the PhiMech CLI, upload the result. No WinForms and no SolidWorks here (the STL export is
// the host's, see Commands/PrintExport.cs), so every step is tested with a fake HTTP handler and a
// fake CLI. Each step runs only because the user clicked its button; the add-in
// never starts a print (MoonrakerClient's safety contract).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhiMech.SolidWorks.Print
{
    /// <summary>Runs `phimech <args>`: (exit, stdout, whole stderr). Exit -1 = timed out.</summary>
    public delegate (int exit, string stdout, string stderr) PhiMechRunner(string arguments, string workDir, int timeoutMs,
                                                                         IDictionary<string, string> environment);

    /// <summary>The one Print-tab setting: the printer URL ("" = PHIMECH_PRINTER_URL, then the configured printer).</summary>
    public sealed class PrintSettings
    {
        public string PrinterUrl = "";
        public double[] EnvelopeMm = (double[])FitCheck.DefaultEnvelope.Clone();
        public string ProfileDirectory = ""; // optional local OrcaSlicer profile directory

        public static string DefaultPath => Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PhiMech", "print-settings.json");

        public static PrintSettings Load(string path = null)
        {
            try
            {
                var p = path ?? DefaultPath;
                if (!File.Exists(p)) return new PrintSettings();
                var o = JObject.Parse(File.ReadAllText(p));
                var settings = new PrintSettings { PrinterUrl = (string)o["printer_url"] ?? "", ProfileDirectory = (string)o["profile_directory"] ?? "" };
                if (o["envelope_mm"] is JArray volume && volume.Count == 3)
                {
                    var values = volume.ToObject<double[]>();
                    if (values.All(v => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v))) settings.EnvelopeMm = values;
                }
                return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return new PrintSettings();
            }
        }

        /// <summary>Written only when the user saves a printer address in the tab.</summary>
        public void Save(string path = null)
        {
            var p = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, new JObject { ["printer_url"] = PrinterUrl ?? "", ["envelope_mm"] = new JArray(EnvelopeMm), ["profile_directory"] = ProfileDirectory ?? "" }.ToString(Formatting.Indented) + "\n");
        }
    }

    public sealed class SliceOutcome
    {
        public bool Ok;
        public string Message;
        public string Material;
        public SliceSummary Summary;
        /// <summary>The kept file: <part folder>\print\<part>-<material>-<sha8>.gcode.3mf.</summary>
        public string GcodePath;
        public string FileName;
        public long SizeBytes;
        public string Sha256;
        public string StlPath;
        public string WorkDir;
        public FitResult Fit;
        /// <summary>Set when slicing failed because OrcaSlicer is missing (the message says how to get it).</summary>
        public bool OrcaMissing;
    }

    public sealed class PrintFlow
    {
        private readonly PrintSettings _settings;
        private readonly Func<HttpMessageHandler> _handler;
        private readonly PhiMechRunner _cli;
        private readonly Func<bool> _cliInstalled;
        private readonly Func<OrcaLocation> _findOrca;
        private readonly Func<string, string> _env;

        /// <param name="settings">the saved printer URL</param>
        /// <param name="handler">HTTP transport per request (tests: a fake; null: the network)</param>
        /// <param name="cli">the PhiMech CLI (null: PhiMechCli.RunFull)</param>
        /// <param name="cliInstalled">is the CLI there (null: PhiMechCli.Installed)</param>
        /// <param name="findOrca">where OrcaSlicer is (null: PrintSlicer.FindOrca on this PC)</param>
        /// <param name="environment">environment lookup (null: the process environment)</param>
        public PrintFlow(PrintSettings settings = null, Func<HttpMessageHandler> handler = null, PhiMechRunner cli = null,
                         Func<bool> cliInstalled = null, Func<OrcaLocation> findOrca = null, Func<string, string> environment = null)
        {
            _settings = settings ?? new PrintSettings();
            _handler = handler;
            _cli = cli ?? ((a, w, t, e) => PhiMechCli.RunFull(a, w, t, e));
            _cliInstalled = cliInstalled ?? (() => PhiMechCli.Installed);
            _findOrca = findOrca ?? (() => PrintSlicer.FindOrca());
            _env = environment ?? System.Environment.GetEnvironmentVariable;
        }

        public PrintSettings Settings => _settings;

        public PrinterUrlChoice Url => PrinterUrl.Resolve(_settings.PrinterUrl, _env);

        public PrinterCheck LastCheck { get; private set; }

        public string LastCheckError { get; private set; }

        public SliceOutcome LastSlice { get; private set; }

        public UploadOutcome LastUpload { get; private set; }

        public bool CliInstalled => Safe(_cliInstalled);

        private MoonrakerClient Client()
        {
            var url = Url;
            if (url.Url == null) throw new PrinterException(PrinterProblem.Unreachable, "Printer address refused: " + url.Problem);
            return new MoonrakerClient(url.Url, _handler?.Invoke());
        }

        // ---- 1. check the printer (read-only GETs) ------------------------------------------------

        public async Task<PrinterCheck> CheckPrinterAsync()
        {
            try
            {
                using (var c = Client())
                {
                    var check = await c.CheckAsync().ConfigureAwait(false);
                    LastCheck = check;
                    LastCheckError = null;
                    return check;
                }
            }
            catch (PrinterException ex)
            {
                LastCheck = null;
                LastCheckError = ex.Message;
                throw;
            }
        }

        /// <summary>The envelope the fit check uses: the live printer value, else the configured example.</summary>
        public double[] Envelope => LastCheck?.Envelope?.AsArray() ?? _settings.EnvelopeMm;

        public string EnvelopeSource => LastCheck?.Envelope != null
            ? LastCheck.Envelope.SourceText
            : "configured or example envelope (not verified); Check printer reads the real one";

        // ---- 2. fit (pure) -------------------------------------------------------------------------

        public FitResult Fit(double[] partMm) => FitCheck.Evaluate(partMm, Envelope, EnvelopeSource);

        /// <summary>
        /// PhiMech's own reading of the printer through the CLI (`phimech print status --json`): the
        /// envelope its machine gates use, with its provenance. Null when the CLI is not installed.
        /// Blocking (up to ~45 s when the printer is unreachable); call it off the UI thread.
        /// </summary>
        public PhiMechPrinterReading PhiMechReading()
        {
            if (!CliInstalled) return null;
            var url = Url;
            if (url.Url == null) return null;
            var run = _cli("print status --url " + url.Url + " --json", Path.GetTempPath(), 60000, null);
            if (run.exit != 0) return new PhiMechPrinterReading { Error = "phimech print status: " + PrintSlicer.ErrorLine(run.stderr) };
            try
            {
                return PrintSlicer.ParseStatus(run.stdout);
            }
            catch (FormatException ex)
            {
                return new PhiMechPrinterReading { Error = ex.Message };
            }
        }

        // ---- 3. slice (PhiMech CLI -> OrcaSlicer) --------------------------------------------------

        /// <summary>A fresh working folder for one slice: <part folder>\print\work\<part>-<material>-<UTC stamp>.</summary>
        public static string WorkDir(string partPath, string partTitle, string material, DateTime utc) =>
            Path.Combine(Path.GetDirectoryName(partPath) ?? "", "print", "work",
                Contract.Slug.From(Path.GetFileNameWithoutExtension(partPath) ?? partTitle) + "-" + Contract.Slug.From(material)
                + "-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        /// <summary>
        /// Slice an STL the host exported into <paramref name="workDir"/>. Blocking: run it off the UI
        /// thread. Never throws for an expected failure; the outcome says what happened.
        /// </summary>
        public SliceOutcome Slice(string stlPath, string partPath, string partTitle, string material, string workDir)
        {
            var o = new SliceOutcome { Material = material, StlPath = stlPath, WorkDir = workDir };
            LastSlice = o;
            try
            {
                var box = StlBounds.Read(stlPath);
                o.Fit = Fit(box.Size);
                if (!o.Fit.FitsAsExported)
                {
                    o.Message = o.Fit.Headline + ". " + o.Fit.Detail;
                    return o;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                o.Message = "Could not read the exported STL (" + ex.Message + "). Nothing was sliced.";
                return o;
            }
            if (!CliInstalled)
            {
                o.Message = PrintSlicer.CliMissingText(PhiMechCli.Command());
                return o;
            }
            var profileDir = !string.IsNullOrWhiteSpace(_settings.ProfileDirectory) ? _settings.ProfileDirectory : _env("PHIMECH_ORCA_PROFILES");
            if (string.IsNullOrWhiteSpace(profileDir) || !Directory.Exists(profileDir))
            {
                o.Message = "Configure a local slicer profile directory (profile_directory or PHIMECH_ORCA_PROFILES) before slicing; nothing was sliced.";
                return o;
            }
            var orca = _findOrca();
            if (orca != null)
            {
                orca.Profiles = profileDir;
                orca.Environment["PHIMECH_ORCA_PROFILES"] = profileDir;
            }
            if (orca == null)
            {
                o.OrcaMissing = true;
                o.Message = PrintSlicer.MissingText;
                return o;
            }
            var outDir = Path.Combine(workDir, "sliced");
            var run = _cli(PrintSlicer.Arguments(stlPath, outDir, material), workDir, PrintSlicer.TimeoutMs, orca.Environment);
            if (run.exit == -1)
            {
                o.Message = "Slicing took longer than " + PrintSlicer.TimeoutMs / 60000 + " minutes and was stopped. Nothing was kept.";
                return o;
            }
            SliceSummary s;
            try
            {
                s = PrintSlicer.ParseResult(run.stdout);
            }
            catch (FormatException)
            {
                var why = PrintSlicer.ErrorLine(run.stderr);
                o.Message = "PhiMech could not slice the part (exit " + run.exit + (why.Length > 0 ? ": " + why : "") + ")."
                            + (why.IndexOf("OrcaSlicer not found", StringComparison.OrdinalIgnoreCase) >= 0
                               || why.IndexOf("profiles not found", StringComparison.OrdinalIgnoreCase) >= 0
                                ? " " + PrintSlicer.MissingText : "");
                o.OrcaMissing = why.IndexOf("OrcaSlicer not found", StringComparison.OrdinalIgnoreCase) >= 0;
                return o;
            }
            o.Summary = s;
            var produced = Path.IsPathRooted(s.Gcode3mf) ? s.Gcode3mf : Path.Combine(outDir, s.Gcode3mf);
            if (!File.Exists(produced))
            {
                // PhiMech's rule: success is the .gcode.3mf existing, whatever the exit code said.
                o.Message = "The slicer reported success but wrote no .gcode.3mf (" + produced + "). Nothing was kept.";
                return o;
            }
            var bytes = File.ReadAllBytes(produced);
            o.Sha256 = MoonrakerClient.Sha256Hex(bytes);
            o.SizeBytes = bytes.LongLength;
            o.FileName = PrintNaming.FileName(partTitle, material, o.Sha256);
            var keep = Path.Combine(Path.GetDirectoryName(partPath) ?? "", "print");
            Directory.CreateDirectory(keep);
            o.GcodePath = Path.Combine(keep, o.FileName);
            if (File.Exists(o.GcodePath))
            {
                if (MoonrakerClient.Sha256Hex(File.ReadAllBytes(o.GcodePath)) != o.Sha256)
                {
                    o.Message = "A different file named " + o.FileName + " is already in " + keep + "; it was left alone and nothing was kept.";
                    o.GcodePath = null;
                    return o;
                }
            }
            else
            {
                File.Copy(produced, o.GcodePath);
            }
            o.Ok = true;
            o.Message = "Sliced for the configured printer in " + material + ": " + s.TimeText + ", " + s.FilamentText
                        + (s.LayerCount != null ? ", " + s.LayerCount.Value.ToString(CultureInfo.InvariantCulture) + " layers" : "")
                        + ". Kept as " + o.GcodePath + ".";
            return o;
        }

        // ---- 4. upload (the one write; never a print) ----------------------------------------------

        /// <summary>What the confirmation dialog names before anything is sent.</summary>
        public string ConfirmationText(SliceOutcome slice)
        {
            var host = PrinterUrl.HostOf(Url.Url);
            var lines = new List<string>
            {
                "Printer: configured printer at " + (Url.Url ?? "?"),
                "File: " + slice.FileName + " (" + PrintFormat.Bytes(slice.SizeBytes) + ")",
                "Goes to: gcodes/" + MoonrakerClient.Folder + "/ on " + host + ". It is stored only: no print is started.",
            };
            var st = LastCheck?.Status;
            if (st != null && st.Busy)
                lines.Add("The printer is " + (st.Paused ? "paused on" : "printing") + " another job"
                          + (string.IsNullOrEmpty(st.Filename) ? "" : " (" + st.Filename + ")")
                          + ". Uploading only stores a file; that job is not touched.");
            return string.Join("\n", lines);
        }

        public async Task<UploadOutcome> UploadAsync(SliceOutcome slice)
        {
            if (slice == null || !slice.Ok || slice.GcodePath == null) throw new InvalidOperationException("slice the part first");
            using (var c = Client())
            {
                var outcome = await c.UploadAsync(slice.GcodePath, slice.FileName, LastCheck?.Status?.Busy == true).ConfigureAwait(false);
                LastUpload = outcome;
                return outcome;
            }
        }

        /// <summary>The sentence the tab shows after an upload: where it is and how a print gets started.</summary>
        public static string AfterUploadText(UploadOutcome u)
        {
            var name = u.RemotePath ?? "";
            var head = u.AlreadyThere
                ? "Already on the printer as " + name + " (same content); nothing was uploaded."
                : "Uploaded to the printer as " + name + " (" + PrintFormat.Bytes(u.SizeBytes) + "). No print was started.";
            if (u.PrintStarted)
                head += " WARNING: the printer reports that a print started. PhiMech did not ask for one; check the printer now.";
            if (u.PrinterWasBusy) head += " The printer is busy with another job, which was not touched.";
            return head + " Start it only after verifying the profile and machine settings on the printer itself.";
        }

        private static bool Safe(Func<bool> f)
        {
            try
            {
                return f();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
