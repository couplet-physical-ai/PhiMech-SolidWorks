// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Slicing goes through the PhiMech CLI across a process boundary, never by
// linking PhiMech or driving OrcaSlicer directly: PhiMech resolves the configured printer profiles and
// flattens their inherits chain, which a raw OrcaSlicer call silently gets wrong (it slices PETG
// as PLA at 200 °C). The command, at PhiMech origin/dev:
//
//   phimech print slice <stl> -o <dir> --material PETG|PLA --quality standard --json
//
// prints SliceResult.to_dict() as JSON: gcode_3mf, print_time_s, print_time, filament_g,
// filament_mm, layer_count, layer_height, nozzle_temp_c, bed_temp_c, filament_type, machine,
// process, filament, seconds, warnings. The printer is always configured printer in that command.
//
// PhiMech finds OrcaSlicer through PHIMECH_ORCASLICER and its profiles through
// PHIMECH_ORCA_PROFILES (its built-in fallbacks are macOS paths). Both settings are passed to a
// separately installed CLI process.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhiMech.SolidWorks.Print
{
    public sealed class SliceSummary
    {
        public string Gcode3mf;
        public double? PrintTimeS;
        public string PrintTimeText;
        public double? FilamentG;
        public double? FilamentMm;
        public int? LayerCount;
        public double? LayerHeight;
        public int? NozzleTempC;
        public int? BedTempC;
        public string FilamentType;
        public string Machine;
        public string Process;
        public string Filament;
        public List<string> Warnings = new List<string>();

        public string TimeText => PrintTimeText ?? (PrintTimeS != null ? PrintFormat.Duration(PrintTimeS.Value) : "unknown");

        public string FilamentText =>
            (FilamentMm != null ? (FilamentMm.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m" : "? m")
            + " · " + (FilamentG != null ? FilamentG.Value.ToString("0.0", CultureInfo.InvariantCulture) + " g" : "? g");
    }

    public sealed class OrcaLocation
    {
        public string Exe;
        public string Profiles;
        /// <summary>Where it was found, for the message: "phimech.cmd", PHIMECH_ORCASLICER, or the folder.</summary>
        public string Source;
        /// <summary>Environment for the PhiMech child process (empty when phimech.cmd sets it itself).</summary>
        public Dictionary<string, string> Environment = new Dictionary<string, string>();
    }

    /// <summary>PhiMech's own reading of the printer (phimech print status --json), for the fit card.</summary>
    public sealed class PhiMechPrinterReading
    {
        public bool Reachable;
        public double[] BuildVolumeMm;
        public string BuildVolumeSource;
        public string KlippyState;
        public string Error;
    }

    public static class PrintSlicer
    {
        public const string OrcaVersion = "2.4.2";
        public const string OrcaReleaseUrl = "https://github.com/OrcaSlicer/OrcaSlicer/releases/tag/v2.4.2";
        public static readonly string[] Materials = { "PETG", "PLA" };
        public const string Quality = "standard";
        /// <summary>A slice of a large part can take minutes; PhiMech's own limit is 600 s.</summary>
        public const int TimeoutMs = 660000;

        public static string MissingText => "OrcaSlicer or your profile directory is missing. Configure PHIMECH_ORCASLICER and PHIMECH_ORCA_PROFILES; nothing was sliced.";

        public static string CliMissingText(string command) => "Set PHIMECH_CMD to a compatible locally installed slicer CLI before slicing; nothing was sliced.";

        /// <summary>The CLI arguments for one slice. Paths must be absolute (PhiMech's own rule for OrcaSlicer).</summary>
        public static string Arguments(string stlPath, string outDir, string material)
        {
            if (!Materials.Contains(material)) throw new ArgumentException("material must be PETG or PLA", nameof(material));
            foreach (var p in new[] { stlPath, outDir })
            {
                if (string.IsNullOrEmpty(p) || !Path.IsPathRooted(p)) throw new ArgumentException("slice paths must be absolute: " + p);
                if (p.IndexOfAny(new[] { '"', '%', '\r', '\n' }) >= 0)
                    throw new ArgumentException("the path contains a character the command line cannot carry (\" or %): " + p);
            }
            return "print slice " + Q(stlPath) + " -o " + Q(outDir.TrimEnd('\\', '/')) + " --material " + material
                   + " --quality " + Quality + " --json";
        }

        private static string Q(string s) => "\"" + s + "\"";

        /// <summary>SliceResult JSON (stdout of --json) -> the numbers. FormatException when there is none.</summary>
        public static SliceSummary ParseResult(string stdout)
        {
            var o = FirstJsonObject(stdout) ?? throw new FormatException("PhiMech printed no slice result");
            var s = new SliceSummary
            {
                Gcode3mf = Str(o["gcode_3mf"]),
                PrintTimeS = Num(o["print_time_s"]),
                PrintTimeText = Str(o["print_time"]),
                FilamentG = Num(o["filament_g"]),
                FilamentMm = Num(o["filament_mm"]),
                LayerCount = Num(o["layer_count"]) is double l ? (int?)(int)Math.Round(l) : null,
                LayerHeight = Num(o["layer_height"]),
                NozzleTempC = Num(o["nozzle_temp_c"]) is double n ? (int?)(int)Math.Round(n) : null,
                BedTempC = Num(o["bed_temp_c"]) is double b ? (int?)(int)Math.Round(b) : null,
                FilamentType = Str(o["filament_type"]),
                Machine = Str(o["machine"]),
                Process = Str(o["process"]),
                Filament = Str(o["filament"]),
            };
            if (o["warnings"] is JArray w) s.Warnings.AddRange(w.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrEmpty(s.Gcode3mf)) throw new FormatException("PhiMech's slice result names no .gcode.3mf");
            return s;
        }

        /// <summary>phimech print status --json -> reachable, envelope and its provenance.</summary>
        public static PhiMechPrinterReading ParseStatus(string stdout)
        {
            var o = FirstJsonObject(stdout) ?? throw new FormatException("PhiMech printed no printer status");
            var r = new PhiMechPrinterReading
            {
                Reachable = o["reachable"]?.Type == JTokenType.Boolean && (bool)o["reachable"],
                BuildVolumeSource = Str(o["build_volume_source"]),
                KlippyState = Str(o["klippy_state"]),
                Error = Str(o["error"]),
            };
            if (o["build_volume_mm"] is JArray a && a.Count == 3 && a.All(x => Num(x) != null))
                r.BuildVolumeMm = a.Select(x => Num(x).Value).ToArray();
            return r;
        }

        /// <summary>The most useful line of PhiMech's stderr: the last "error:" line, else the last line.</summary>
        public static string ErrorLine(string stderr)
        {
            var lines = (stderr ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return "";
            var err = lines.LastOrDefault(l => Regex.IsMatch(l, @"(^|\b)(error|SliceError|ProfileError)\b[:]", RegexOptions.IgnoreCase));
            return err ?? lines.Last();
        }

        /// <summary>
        /// Find OrcaSlicer: PHIMECH_ORCASLICER; phimech.cmd setting it (the installer's line); a local installation beside the configured CLI; then Program Files. Null when there is none.
        /// </summary>
        public static OrcaLocation FindOrca(Func<string, string> environment, Func<string, bool> fileExists, Func<string, bool> dirExists,
                                            string installRoot, string programFiles, string phimechCmdText)
        {
            var env = environment?.Invoke("PHIMECH_ORCASLICER");
            if (!string.IsNullOrWhiteSpace(env) && fileExists(env))
            {
                var loc = new OrcaLocation { Exe = env, Source = "PHIMECH_ORCASLICER" };
                var profiles = environment("PHIMECH_ORCA_PROFILES");
                loc.Profiles = !string.IsNullOrWhiteSpace(profiles) ? profiles : ProfilesBeside(env);
                if (string.IsNullOrWhiteSpace(profiles) && dirExists(loc.Profiles)) loc.Environment["PHIMECH_ORCA_PROFILES"] = loc.Profiles;
                return loc;
            }
            var fromCmd = CmdValue(phimechCmdText, "PHIMECH_ORCASLICER");
            if (fromCmd != null && fileExists(fromCmd))
                return new OrcaLocation { Exe = fromCmd, Profiles = CmdValue(phimechCmdText, "PHIMECH_ORCA_PROFILES") ?? ProfilesBeside(fromCmd), Source = "phimech.cmd" };
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(installRoot)) candidates.Add(Path.Combine(installRoot, "prereqs", "orcaslicer-" + OrcaVersion, "orca-slicer.exe"));
            if (!string.IsNullOrEmpty(programFiles)) candidates.Add(Path.Combine(programFiles, "OrcaSlicer", "orca-slicer.exe"));
            foreach (var exe in candidates)
            {
                if (!fileExists(exe)) continue;
                var profiles = ProfilesBeside(exe);
                if (!dirExists(profiles)) continue;
                var loc = new OrcaLocation { Exe = exe, Profiles = profiles, Source = Path.GetDirectoryName(exe) };
                loc.Environment["PHIMECH_ORCASLICER"] = exe;
                loc.Environment["PHIMECH_ORCA_PROFILES"] = profiles;
                return loc;
            }
            return null;
        }

        /// <summary>The live machine: the real environment, disk and the installed phimech.cmd.</summary>
        public static OrcaLocation FindOrca()
        {
            var cmd = PhiMechCli.Command();
            string text = null;
            try
            {
                if (File.Exists(cmd)) text = File.ReadAllText(cmd);
            }
            catch (IOException)
            {
                text = null;
            }
            catch (UnauthorizedAccessException)
            {
                text = null;
            }
            return FindOrca(System.Environment.GetEnvironmentVariable, File.Exists, Directory.Exists, Path.GetDirectoryName(cmd),
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), text);
        }

        /// <summary>`set "NAME=value"` (optionally after `if not defined NAME`) in a .cmd file -> value.</summary>
        public static string CmdValue(string cmdText, string name)
        {
            if (string.IsNullOrEmpty(cmdText)) return null;
            var m = Regex.Match(cmdText, @"set\s+""" + Regex.Escape(name) + @"=([^""\r\n]+)""", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static string ProfilesBeside(string exe) => Path.Combine(Path.GetDirectoryName(exe) ?? "", "resources", "profiles");

        private static JObject FirstJsonObject(string text)
        {
            var t = text ?? "";
            var start = t.IndexOf('{');
            var end = t.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try
            {
                return JToken.Parse(t.Substring(start, end - start + 1)) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;

        private static double? Num(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return (double)t;
            return t.Type == JTokenType.String && double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : (double?)null;
        }
    }
}
