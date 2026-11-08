// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// What the Print tab reads back from Moonraker, and the parsing of its JSON. Pure: every parser
// takes the response text and is tested against real responses recorded from the configured printer
// (Moonraker JSON responses). Unknown fields are ignored; a missing field is null, never a guess.
//
// Shapes (Moonraker API 1.4, according to the public Moonraker API documentation):
//   GET /printer/info                  {"result": {"state": "ready", "state_message", "hostname", ...}}
//   GET /printer/objects/query?...     {"result": {"eventtime", "status": {"print_stats": {...}, ...}}}
//   GET /printer/objects/query?configfile=settings
//                                      {"result": {"status": {"configfile": {"settings": {"bed_mesh": ...}}}}}
//   GET /server/files/directory?path=  {"result": {"dirs", "files": [{"filename", "size", "modified"}], "disk_usage"}}
//   errors                             {"error": {"code": 404, "message": "..."}}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhiMech.SolidWorks.Print
{
    /// <summary>GET /printer/info: is Klipper itself up?</summary>
    public sealed class PrinterInfo
    {
        /// <summary>Klippy's state: ready, startup, shutdown, error (lower case, as Moonraker sends it).</summary>
        public string KlippyState;
        public string StateMessage;
        public string Hostname;

        public bool Ready => string.Equals(KlippyState, "ready", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Heater
    {
        public double? Temperature;
        public double? Target;

        public string Text =>
            Temperature == null ? "unknown"
            : Fmt(Temperature.Value) + (Target != null && Target.Value > 0 ? " / " + Fmt(Target.Value) : "") + " °C";

        private static string Fmt(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>GET /printer/objects/query?print_stats&display_status&heater_bed&extruder&toolhead&webhooks.</summary>
    public sealed class PrinterStatus
    {
        /// <summary>print_stats.state: standby, printing, paused, complete, cancelled, error.</summary>
        public string PrintState;
        public string Filename;
        public string PrintMessage;
        /// <summary>0..1 from display_status.progress (what the printer's screen shows).</summary>
        public double? Progress;
        public double? PrintDurationS;
        public int? CurrentLayer;
        public int? TotalLayers;
        public Heater Nozzle = new Heater();
        public Heater Bed = new Heater();
        public string WebhooksState;
        public string WebhooksMessage;
        public double[] AxisMinimum;
        public double[] AxisMaximum;
        public string HomedAxes;

        public bool Printing => Is("printing");
        public bool Paused => Is("paused");
        public bool Error => Is("error");

        /// <summary>A file is loaded and not finished: the printer is busy with someone's job.</summary>
        public bool Busy => Printing || Paused;

        private bool Is(string s) => string.Equals(PrintState, s, StringComparison.OrdinalIgnoreCase);
    }

    public enum EnvelopeSource
    {
        /// <summary>Bed mesh area (X, Y) and the Z axis maximum, read from this printer's config.</summary>
        BedMesh,
        /// <summary>Axis travel from the toolhead (no bed mesh in the config): an upper bound.</summary>
        AxisTravel,
    }

    /// <summary>The part envelope, as PhiMech derives it: the levelled bed-mesh area and the Z travel.</summary>
    public sealed class BuildVolume
    {
        public BuildVolume(double x, double y, double z, EnvelopeSource source)
        {
            X = x;
            Y = y;
            Z = z;
            Source = source;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public EnvelopeSource Source { get; }

        public double[] AsArray() => new[] { X, Y, Z };

        public string Text => Mm(X) + " × " + Mm(Y) + " × " + Mm(Z) + " mm";

        public string SourceText => Source == EnvelopeSource.BedMesh
            ? "bed mesh area and Z travel, read from the printer"
            : "axis travel, read from the printer (no bed mesh configured)";

        public static string Mm(double v) =>
            Math.Abs(v - Math.Round(v)) < 0.05
                ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture)
                : v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>The machine facts from configfile.settings the tab shows (all optional).</summary>
    public sealed class MachineConfig
    {
        public double[] MeshMin;
        public double[] MeshMax;
        public double? ZMax;
        public double? NozzleDiameterMm;
        public double? ExtruderMaxC;
        public double? BedMaxC;
        public double? ChamberMaxC;
        public string Kinematics;
    }

    public sealed class RemoteFile
    {
        public string Name;
        public long Size;
        public double Modified;
    }

    public sealed class RemoteDirectory
    {
        public List<RemoteFile> Files = new List<RemoteFile>();
        public long? FreeBytes;

        public RemoteFile Find(string name) =>
            Files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
    }

    public sealed class MoonrakerError
    {
        public int? Code;
        public string Message;
    }

    /// <summary>The parsers. Each throws FormatException only when the text is not Moonraker JSON at all.</summary>
    public static class Moonraker
    {
        public static PrinterInfo ParseInfo(string json)
        {
            var r = Result(json) as JObject ?? throw new FormatException("no \"result\" object in /printer/info");
            return new PrinterInfo
            {
                KlippyState = Str(r["state"]),
                StateMessage = Str(r["state_message"]),
                Hostname = Str(r["hostname"]),
            };
        }

        public static PrinterStatus ParseStatus(string json)
        {
            var status = (Result(json) as JObject)?["status"] as JObject
                         ?? throw new FormatException("no \"result.status\" object in /printer/objects/query");
            var s = new PrinterStatus();
            if (status["print_stats"] is JObject ps)
            {
                s.PrintState = Str(ps["state"]);
                s.Filename = Str(ps["filename"]);
                s.PrintMessage = Str(ps["message"]);
                s.PrintDurationS = Num(ps["print_duration"]);
                if (ps["info"] is JObject info)
                {
                    s.CurrentLayer = Int(info["current_layer"]);
                    s.TotalLayers = Int(info["total_layer"]);
                }
            }
            if (status["display_status"] is JObject ds) s.Progress = Num(ds["progress"]);
            if (status["extruder"] is JObject ex) s.Nozzle = HeaterOf(ex);
            if (status["heater_bed"] is JObject bed) s.Bed = HeaterOf(bed);
            if (status["webhooks"] is JObject wh)
            {
                s.WebhooksState = Str(wh["state"]);
                s.WebhooksMessage = Str(wh["state_message"]);
            }
            if (status["toolhead"] is JObject th)
            {
                s.AxisMinimum = Vec(th["axis_minimum"], 3);
                s.AxisMaximum = Vec(th["axis_maximum"], 3);
                s.HomedAxes = Str(th["homed_axes"]);
            }
            return s;
        }

        public static MachineConfig ParseConfig(string json)
        {
            var settings = ((Result(json) as JObject)?["status"]?["configfile"] as JObject)?["settings"] as JObject
                           ?? throw new FormatException("no \"result.status.configfile.settings\" object");
            var c = new MachineConfig();
            if (settings["bed_mesh"] is JObject mesh)
            {
                c.MeshMin = Vec(mesh["mesh_min"], 2);
                c.MeshMax = Vec(mesh["mesh_max"], 2);
            }
            if (settings["stepper_z"] is JObject z) c.ZMax = Num(z["position_max"]);
            if (settings["extruder"] is JObject ex)
            {
                c.NozzleDiameterMm = Num(ex["nozzle_diameter"]);
                c.ExtruderMaxC = Num(ex["max_temp"]);
            }
            if (settings["heater_bed"] is JObject bed) c.BedMaxC = Num(bed["max_temp"]);
            if ((settings["heater_generic chamber"] ?? settings["chamber"]) is JObject ch) c.ChamberMaxC = Num(ch["max_temp"]);
            if (settings["printer"] is JObject p) c.Kinematics = Str(p["kinematics"]);
            return c;
        }

        /// <summary>
        /// The part envelope, derived the way PhiMech's printer_info() does: X and Y from the bed
        /// mesh area (a part outside it sits on unmeasured bed), Z from the Z axis maximum. Without a
        /// bed mesh, the toolhead's axis travel - an upper bound, and labelled so. Null when neither
        /// is known.
        /// </summary>
        public static BuildVolume Envelope(MachineConfig config, PrinterStatus status)
        {
            var zMax = config?.ZMax ?? (status?.AxisMaximum != null ? status.AxisMaximum[2] : (double?)null);
            if (config?.MeshMin != null && config.MeshMax != null && zMax != null)
            {
                var x = config.MeshMax[0] - config.MeshMin[0];
                var y = config.MeshMax[1] - config.MeshMin[1];
                if (x > 0 && y > 0 && zMax.Value > 0) return new BuildVolume(x, y, zMax.Value, EnvelopeSource.BedMesh);
            }
            if (status?.AxisMinimum != null && status.AxisMaximum != null)
            {
                var x = status.AxisMaximum[0] - Math.Max(0, status.AxisMinimum[0]);
                var y = status.AxisMaximum[1] - Math.Max(0, status.AxisMinimum[1]);
                var z = status.AxisMaximum[2];
                if (x > 0 && y > 0 && z > 0) return new BuildVolume(x, y, z, EnvelopeSource.AxisTravel);
            }
            return null;
        }

        public static RemoteDirectory ParseDirectory(string json)
        {
            var r = Result(json) as JObject ?? throw new FormatException("no \"result\" object in /server/files/directory");
            var d = new RemoteDirectory();
            if (r["files"] is JArray files)
            {
                foreach (var f in files.OfType<JObject>())
                {
                    var name = Str(f["filename"]);
                    if (name == null) continue;
                    d.Files.Add(new RemoteFile
                    {
                        Name = name,
                        Size = (long)(Num(f["size"]) ?? 0),
                        Modified = Num(f["modified"]) ?? 0,
                    });
                }
            }
            if (r["disk_usage"] is JObject du && Num(du["free"]) is double free) d.FreeBytes = (long)free;
            return d;
        }

        /// <summary>{"error": {"code", "message"}} -> the error, or null when the body is not one.</summary>
        public static MoonrakerError ParseError(string json)
        {
            try
            {
                if (!(JToken.Parse(json ?? "") is JObject o) || !(o["error"] is JObject e)) return null;
                return new MoonrakerError { Code = Int(e["code"]), Message = Str(e["message"]) };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static JToken Result(string json)
        {
            JToken root;
            try
            {
                root = JToken.Parse(json ?? "");
            }
            catch (JsonException ex)
            {
                throw new FormatException("the printer's answer is not JSON: " + ex.Message, ex);
            }
            return (root as JObject)?["result"];
        }

        private static Heater HeaterOf(JObject o) => new Heater { Temperature = Num(o["temperature"]), Target = Num(o["target"]) };

        private static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;

        private static double? Num(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return (double)t;
            return t.Type == JTokenType.String && double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : (double?)null;
        }

        private static int? Int(JToken t)
        {
            var n = Num(t);
            return n == null ? (int?)null : (int)Math.Round(n.Value);
        }

        private static double[] Vec(JToken t, int n)
        {
            if (!(t is JArray a) || a.Count < n) return null;
            var v = new double[n];
            for (var i = 0; i < n; i++)
            {
                var x = Num(a[i]);
                if (x == null) return null;
                v[i] = x.Value;
            }
            return v;
        }
    }
}
