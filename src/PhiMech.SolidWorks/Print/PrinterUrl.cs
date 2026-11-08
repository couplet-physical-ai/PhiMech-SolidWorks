// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Which printer the Print tab talks to, and whether that address is one it may talk to at all.
// the configured printer is a configured printer running Klipper with Moonraker on port 7125 on your configured local network.
// Order: the URL saved in the Print tab's settings, then PHIMECH_PRINTER_URL (the same variable
// PhiMech's own print path reads), then the configured printer.
//
// Validation is deliberately narrow: http(s) only, no user name or password, no path, query or
// fragment (a Moonraker base URL is scheme://host:port), and an IP literal must be private,
// loopback or link-local. A host name is accepted (configured printer, printer.lan): the your configured local network is
// what resolves it. No port means Moonraker's 7125, not the scheme's 80/443.

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PhiMech.SolidWorks.Print
{
    public enum PrinterUrlSource
    {
        Default,
        Environment,
        Settings,
    }

    public sealed class PrinterUrlChoice
    {
        public PrinterUrlChoice(string url, PrinterUrlSource source, string problem)
        {
            Url = url;
            Source = source;
            Problem = problem;
        }

        /// <summary>The normalised base URL (scheme://host:port), or null when it was refused.</summary>
        public string Url { get; }

        public PrinterUrlSource Source { get; }

        /// <summary>Why the configured value was refused (null when it is fine).</summary>
        public string Problem { get; }

        public string SourceText =>
            Source == PrinterUrlSource.Settings ? "saved in the Print tab"
            : Source == PrinterUrlSource.Environment ? "from " + PrinterUrl.EnvVar
            : "the configured printer";
    }

    public static class PrinterUrl
    {
        /// <summary>the configured printer (Moonraker), as in PhiMech's phimech/print/printer.py.</summary>
        public const string Default = "http://127.0.0.1:7125";

        public const string EnvVar = "PHIMECH_PRINTER_URL";

        public const int MoonrakerPort = 7125;

        private static readonly Regex Authority = new Regex(
            @"^(?<scheme>[A-Za-z][A-Za-z0-9+.-]*)://(?<auth>[^/?#]*)(?<rest>.*)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The URL to use: the saved setting, then the environment, then the default. A configured
        /// value that fails validation is reported (Problem) and NOT replaced by the next source:
        /// silently talking to a different printer than the one asked for is worse than refusing.
        /// </summary>
        public static PrinterUrlChoice Resolve(string saved, Func<string, string> environment = null)
        {
            environment = environment ?? System.Environment.GetEnvironmentVariable;
            string raw;
            PrinterUrlSource source;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                raw = saved;
                source = PrinterUrlSource.Settings;
            }
            else
            {
                var env = SafeEnv(environment);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    raw = env;
                    source = PrinterUrlSource.Environment;
                }
                else
                {
                    raw = Default;
                    source = PrinterUrlSource.Default;
                }
            }
            return Normalize(raw, out var url, out var why)
                ? new PrinterUrlChoice(url, source, null)
                : new PrinterUrlChoice(null, source, why);
        }

        private static string SafeEnv(Func<string, string> environment)
        {
            try
            {
                return environment(EnvVar);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// "127.0.0.1" -> "http://127.0.0.1:7125"; "https://printer.lan:443" stays as given.
        /// False, with a reason a person can act on, for anything else.
        /// </summary>
        public static bool Normalize(string raw, out string url, out string why)
        {
            url = null;
            var text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                why = "Enter the printer's address, for example " + Default + ".";
                return false;
            }
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "http://" + text;
            var m = Authority.Match(text);
            if (!m.Success)
            {
                why = "\"" + raw.Trim() + "\" is not a URL. Use http://<printer>:" + MoonrakerPort + ".";
                return false;
            }
            var scheme = m.Groups["scheme"].Value.ToLowerInvariant();
            if (scheme != "http" && scheme != "https")
            {
                why = "Only http:// or https:// printer addresses are allowed (got " + scheme + "://).";
                return false;
            }
            var auth = m.Groups["auth"].Value;
            if (auth.IndexOf('@') >= 0)
            {
                why = "The printer address must not contain a user name or password.";
                return false;
            }
            var rest = m.Groups["rest"].Value;
            if (rest.Length > 0 && rest != "/")
            {
                why = "Give only the printer's base address (scheme://host:port), without a path or query.";
                return false;
            }
            if (!SplitHostPort(auth, out var host, out var port, out why)) return false;
            if (!HostAllowed(host, out why)) return false;
            var shown = host.IndexOf(':') >= 0 ? "[" + host + "]" : host.ToLowerInvariant();
            url = scheme + "://" + shown + ":" + (port ?? MoonrakerPort).ToString(CultureInfo.InvariantCulture);
            why = null;
            return true;
        }

        private static bool SplitHostPort(string auth, out string host, out int? port, out string why)
        {
            host = auth;
            port = null;
            why = null;
            string portText = null;
            if (auth.StartsWith("[", StringComparison.Ordinal))
            {
                var close = auth.IndexOf(']');
                if (close < 0)
                {
                    why = "The IPv6 address is missing its closing ].";
                    return false;
                }
                host = auth.Substring(1, close - 1);
                var after = auth.Substring(close + 1);
                if (after.Length > 0)
                {
                    if (!after.StartsWith(":", StringComparison.Ordinal))
                    {
                        why = "Unexpected text after the IPv6 address.";
                        return false;
                    }
                    portText = after.Substring(1);
                }
            }
            else
            {
                var colon = auth.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = auth.Substring(0, colon);
                    portText = auth.Substring(colon + 1);
                }
            }
            if (host.Length == 0)
            {
                why = "The printer address has no host.";
                return false;
            }
            if (portText != null)
            {
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p < 1 || p > 65535)
                {
                    why = "\"" + portText + "\" is not a port number (Moonraker uses " + MoonrakerPort + ").";
                    return false;
                }
                port = p;
            }
            return true;
        }

        private static readonly Regex HostName = new Regex(
            @"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.?$",
            RegexOptions.CultureInvariant);

        private static readonly Regex DottedNumbers = new Regex(@"^[0-9.]+$", RegexOptions.CultureInvariant);

        private static bool HostAllowed(string host, out string why)
        {
            why = null;
            // "172.16" and "1" also parse as IPv4 (shorthand forms); only the dotted quad is accepted.
            var quad = DottedNumbers.IsMatch(host) && host.Split('.').Length == 4;
            if (IPAddress.TryParse(host, out var ip) && (host.IndexOf(':') >= 0 || quad))
            {
                if (IsPrivate(ip)) return true;
                why = host + " is a public internet address. The printer must be on your configured local network "
                      + "(a local-network address or host name).";
                return false;
            }
            if (DottedNumbers.IsMatch(host))
            {
                why = "\"" + host + "\" is not a valid IP address.";
                return false;
            }
            if (!HostName.IsMatch(host))
            {
                why = "\"" + host + "\" is not a valid host name.";
                return false;
            }
            return true;
        }

        /// <summary>RFC 1918, loopback and link-local IPv4; loopback, unique-local and link-local IPv6.</summary>
        public static bool IsPrivate(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10
                       || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                       || (b[0] == 192 && b[1] == 168)
                       || b[0] == 127
                       || (b[0] == 169 && b[1] == 254);
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal) return true;
                var b = ip.GetAddressBytes();
                return (b[0] & 0xFE) == 0xFC;   // fc00::/7
            }
            return false;
        }

        /// <summary>The host part of a normalised URL, for messages ("127.0.0.1").</summary>
        public static string HostOf(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch (UriFormatException)
            {
                return url ?? "";
            }
        }
    }
}
