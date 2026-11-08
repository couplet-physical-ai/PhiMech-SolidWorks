// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// A file-level readiness check for STEP against contracts/step-profile.md. It reads the text of
// the file and nothing else: no kernel, no SolidWorks. This is the same idea as PhiMech's
// `interop step` (phimech/interop/step.py), re-implemented here independently so the two repos
// share a *contract* and not code. Numbers it cannot establish from text (post-import body
// count, kernel validity) are filled in by ImportStepCommand from the SolidWorks API.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhiMech.SolidWorks.Contract
{
    public enum Verdict
    {
        Ready,
        NotReady,
        Unknown,
    }

    public sealed class Finding
    {
        public Finding(string code, bool fatal, string detail)
        {
            Code = code;
            Fatal = fatal;
            Detail = detail;
        }

        /// <summary>Stable machine-readable code from step-profile.md, e.g. not_millimetres.</summary>
        public string Code { get; }

        /// <summary>True for FAIL, false for WARN.</summary>
        public bool Fatal { get; }

        public string Detail { get; }

        public override string ToString() => (Fatal ? "FAIL " : "warn ") + Code + ": " + Detail;
    }

    /// <summary>Everything the text of a STEP file can tell us about whether it fits the profile.</summary>
    public sealed class StepReadiness
    {
        // Thresholds are the ones PhiMech's `interop risk` uses for `medium`; keep them in step
        // with contracts/step-profile.md rather than tuning them here.
        public const int WarnFaces = 1000;
        public const int WarnEdges = 2000;
        public const long MaxBytesToScan = 64L * 1024 * 1024;

        private static readonly Regex SchemaRe = new Regex(
            @"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex UnitRe = new Regex(
            @"SI_UNIT\s*\(\s*(\.[A-Z]+\.|\$)\s*,\s*\.METRE\.\s*\)", RegexOptions.Compiled);

        private static readonly Regex ConversionUnitRe = new Regex(
            @"CONVERSION_BASED_UNIT\s*\(\s*'([^']*)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FileNameRe = new Regex(
            @"FILE_NAME\s*\(\s*'[^']*'\s*,\s*'[^']*'\s*,\s*\(([^)]*)\)\s*,\s*\(([^)]*)\)\s*,\s*'([^']*)'\s*,\s*'([^']*)'",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public string Path { get; private set; }
        public long Bytes { get; private set; }
        public string Schema { get; private set; } = "";
        public int ApVersion { get; private set; }
        public string LengthUnit { get; private set; } = "";
        public string Originator { get; private set; } = "";
        public string Preprocessor { get; private set; } = "";
        public int Solids { get; private set; }
        public int ClosedShells { get; private set; }
        public int Faces { get; private set; }
        public int Edges { get; private set; }
        public int Tessellated { get; private set; }
        public int AnalyticSurfaces { get; private set; }
        public int SplineSurfaces { get; private set; }
        public List<Finding> Findings { get; } = new List<Finding>();

        /// <summary>Filled in after import by the caller; -1 until then.</summary>
        public int ImportedSolidBodies { get; private set; } = -1;

        public bool UnitsAreMillimetres => LengthUnit == "mm";

        public Verdict Verdict
        {
            get
            {
                if (Findings.Exists(f => f.Code == "unreadable"))
                {
                    return Verdict.Unknown;
                }
                return Findings.Exists(f => f.Fatal) ? Verdict.NotReady : Verdict.Ready;
            }
        }

        /// <summary>Scan a STEP file. Never throws for bad content; a bad file yields Verdict.Unknown.</summary>
        public static StepReadiness Read(string path)
        {
            var r = new StepReadiness { Path = path };
            string text;
            try
            {
                var info = new FileInfo(path);
                r.Bytes = info.Length;
                if (info.Length > MaxBytesToScan)
                {
                    r.Findings.Add(new Finding("large_file", false,
                        "only the first " + MaxBytesToScan + " bytes were scanned"));
                }
                text = ReadHead(path, MaxBytesToScan);
            }
            catch (Exception ex)
            {
                r.Findings.Add(new Finding("unreadable", true, ex.Message));
                return r;
            }

            if (!text.StartsWith("ISO-10303-21", StringComparison.Ordinal))
            {
                r.Findings.Add(new Finding("unreadable", true, "not a STEP Part 21 file (no ISO-10303-21 header)"));
                return r;
            }

            r.ParseHeader(text);
            r.CountEntities(text);
            r.Judge();
            return r;
        }

        /// <summary>Record what SolidWorks actually produced after Import; adds the profile rule 5 findings.</summary>
        public void RecordImportedBodies(int solidBodies)
        {
            ImportedSolidBodies = solidBodies;
            if (solidBodies == 0)
            {
                Findings.Add(new Finding("import_produced_no_solid", true,
                    "the file declares " + Solids + " MANIFOLD_SOLID_BREP but SolidWorks produced no solid body"));
            }
            else if (solidBodies > 1)
            {
                Findings.Add(new Finding("multi_body", false,
                    solidBodies + " solid bodies; an IR `import` source is one envelope"));
            }
        }

        public void RecordImportDiagnostics(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                Findings.Add(new Finding("import_diagnostics", false, text.Trim()));
            }
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(Verdict == Verdict.Ready ? "READY" : Verdict == Verdict.NotReady ? "NOT READY" : "UNKNOWN");
            sb.Append("  ").Append(Path).AppendLine();
            sb.Append("  AP").Append(ApVersion == 0 ? "?" : ApVersion.ToString())
              .Append(" - ").Append(Solids == 1 ? "solid" : Solids + " solids")
              .Append(" - ").Append(string.IsNullOrEmpty(LengthUnit) ? "unit ?" : LengthUnit)
              .Append(" - from ").Append(string.IsNullOrEmpty(Originator) ? "?" : Originator)
              .Append(" - ").Append(Bytes).Append(" bytes").AppendLine();
            sb.Append("  ").Append(Faces).Append(" faces, ").Append(Edges).Append(" edges, ")
              .Append(AnalyticSurfaces).Append(" analytic + ").Append(SplineSurfaces).Append(" spline surfaces");
            if (Tessellated > 0)
            {
                sb.Append(", ").Append(Tessellated).Append(" TESSELLATED entities");
            }
            sb.AppendLine();
            if (ImportedSolidBodies >= 0)
            {
                sb.Append("  SolidWorks imported ").Append(ImportedSolidBodies).Append(" solid body(ies)").AppendLine();
            }
            foreach (var f in Findings)
            {
                sb.Append("    ").Append(f).AppendLine();
            }
            return sb.ToString();
        }

        // ---- internals -------------------------------------------------------------------

        private static string ReadHead(string path, long maxBytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var len = (int)Math.Min(fs.Length, maxBytes);
                var buf = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = fs.Read(buf, read, len - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                // STEP Part 21 is ISO 8859-1 by definition; Latin-1 (code page 28591) never throws
                // on any byte. Encoding.Latin1 is .NET 5+, so use the code page for net48.
                return Encoding.GetEncoding(28591).GetString(buf, 0, read);
            }
        }

        private void ParseHeader(string text)
        {
            var m = SchemaRe.Match(text);
            if (m.Success)
            {
                Schema = m.Groups[1].Value.Trim();
                var up = Schema.ToUpperInvariant();
                if (up.Contains("AUTOMOTIVE_DESIGN") || up.Contains(" 214 "))
                {
                    ApVersion = 214;
                }
                else if (up.Contains("AP242") || up.Contains(" 242 "))
                {
                    ApVersion = 242;
                }
                else if (up.Contains("CONFIG_CONTROL_DESIGN") || up.Contains(" 203 "))
                {
                    ApVersion = 203;
                }
            }

            var fn = FileNameRe.Match(text);
            if (fn.Success)
            {
                Originator = fn.Groups[4].Value.Trim();
                Preprocessor = fn.Groups[3].Value.Trim();
            }

            // Prefer an explicit SI length unit; a CONVERSION_BASED_UNIT('INCH', ...) means inches.
            var conv = ConversionUnitRe.Match(text);
            if (conv.Success)
            {
                LengthUnit = conv.Groups[1].Value.Trim().ToLowerInvariant();
            }
            else
            {
                var u = UnitRe.Match(text);
                if (u.Success)
                {
                    var prefix = u.Groups[1].Value;
                    LengthUnit = prefix == ".MILLI." ? "mm"
                        : prefix == "$" ? "m"
                        : prefix == ".CENTI." ? "cm"
                        : prefix.Trim('.').ToLowerInvariant() + "m";
                }
            }
        }

        private void CountEntities(string text)
        {
            Solids = CountEntity(text, "MANIFOLD_SOLID_BREP");
            ClosedShells = CountEntity(text, "CLOSED_SHELL");
            Faces = CountEntity(text, "ADVANCED_FACE") + CountEntity(text, "FACE_SURFACE");
            Edges = CountEntity(text, "EDGE_CURVE");
            Tessellated = CountEntity(text, "TRIANGULATED_FACE")
                        + CountEntity(text, "TRIANGULATED_SURFACE_SET")
                        + CountEntity(text, "COMPLEX_TRIANGULATED_FACE")
                        + CountEntity(text, "COMPLEX_TRIANGULATED_SURFACE_SET")
                        + CountEntity(text, "TESSELLATED_SOLID")
                        + CountEntity(text, "TESSELLATED_SHELL");
            AnalyticSurfaces = CountEntity(text, "PLANE")
                             + CountEntity(text, "CYLINDRICAL_SURFACE")
                             + CountEntity(text, "CONICAL_SURFACE")
                             + CountEntity(text, "SPHERICAL_SURFACE")
                             + CountEntity(text, "TOROIDAL_SURFACE")
                             + CountEntity(text, "SURFACE_OF_REVOLUTION")
                             + CountEntity(text, "SURFACE_OF_LINEAR_EXTRUSION");
            SplineSurfaces = CountEntity(text, "B_SPLINE_SURFACE_WITH_KNOTS")
                           + CountEntity(text, "B_SPLINE_SURFACE")
                           + CountEntity(text, "BEZIER_SURFACE")
                           + CountEntity(text, "RATIONAL_B_SPLINE_SURFACE");
        }

        /// <summary>Count `= NAME(` occurrences: an entity instance, not a mention inside a string.</summary>
        private static int CountEntity(string text, string name)
        {
            // Match `= NAME(` or `=NAME(`; complex entities like `(A() B())` are rare in
            // OpenCascade output and are counted the same way PhiMech counts them: by name.
            var re = new Regex(@"(?<![A-Z_])" + Regex.Escape(name) + @"\s*\(", RegexOptions.Compiled);
            return re.Matches(text).Count;
        }

        private void Judge()
        {
            if (ApVersion == 0)
            {
                Findings.Add(new Finding("unknown_schema", true,
                    "FILE_SCHEMA is '" + Schema + "', not AP203/AP214/AP242"));
            }
            if (LengthUnit != "mm")
            {
                Findings.Add(new Finding("not_millimetres", true,
                    string.IsNullOrEmpty(LengthUnit) ? "no length unit declared"
                        : "length unit is " + LengthUnit + " (a scaled model passes every geometric check)"));
            }
            if (Tessellated > 0)
            {
                Findings.Add(new Finding("tessellated", true,
                    Tessellated + " TRIANGULATED_*/TESSELLATED_* entities: a graphics mesh, not an editable solid"));
            }
            if (Solids == 0)
            {
                Findings.Add(new Finding("no_solid", true,
                    Faces == 0 ? "no faces at all" : "faces but no MANIFOLD_SOLID_BREP: surfaces only"));
            }
            if (Solids > 1)
            {
                Findings.Add(new Finding("multi_body", false, Solids + " solids declared in the file"));
            }
            if (Faces > WarnFaces || Edges > WarnEdges)
            {
                Findings.Add(new Finding("large_topology", false,
                    Faces + " faces / " + Edges + " edges (> " + WarnFaces + " / " + WarnEdges
                    + "): edits will be slow or fail; treat as a fixed envelope"));
            }
            if (Solids >= 1 && Faces > 0 && AnalyticSurfaces == 0 && SplineSurfaces == 0)
            {
                // Every face planar and no PLANE entity would be odd; more likely a writer we do not know.
                Findings.Add(new Finding("unknown_surfaces", false, "no recognised surface entities"));
            }
            if (SplineSurfaces > 0)
            {
                Findings.Add(new Finding("spline_surfaces", false,
                    SplineSurfaces + " spline surfaces: Recognize Features recovers little from these"));
            }
        }
    }
}
