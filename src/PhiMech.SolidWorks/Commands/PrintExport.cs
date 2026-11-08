// Licensed under Apache-2.0; copyright holder in NOTICE.
//
// The Print tab's only SolidWorks calls: which part is open, and an STL of it in millimetres.
// Everything after the STL (fit, slice, upload) is in Print/ and never touches SolidWorks.
//
// Refuse ambiguous sources: assemblies, unsaved or modified parts,
// or a part that is not exactly one solid body is refused before anything is written. The STL
// export changes two SolidWorks system options (STL unit, binary format) for the duration of the
// export only, and puts the user's values back afterwards.

using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace PhiMech.SolidWorks.Commands
{
    public sealed class PrintPart
    {
        public string Title;
        public string Path;
        /// <summary>The active document is a part (whatever else is wrong with it).</summary>
        public bool IsPart;
        /// <summary>Approximate size (mm) from SolidWorks' part box; the exact one comes from the STL.</summary>
        public double[] ApproxSizeMm;
        /// <summary>Why this part cannot be printed from the tab (null when it can).</summary>
        public string Problem;
    }

    public static class PrintExport
    {
        // Approximate part dimensions in mm; no job-server handoff dependency.
        private static double[] ApproxBoundingBoxMm(IPartDoc part)
        {
            try
            {
                var box = part.GetPartBox(true) as double[];
                if (box == null || box.Length != 6) return null;
                return new[]
                {
                    Math.Round((box[3] - box[0]) * 1000.0, 3),
                    Math.Round((box[4] - box[1]) * 1000.0, 3),
                    Math.Round((box[5] - box[2]) * 1000.0, 3),
                };
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The active part as the Print tab needs it; Problem says why not. Reads only. Without
        /// <paramref name="measure"/> it is cheap (type, path, saved flag) for re-checking the buttons;
        /// with it, the solid bodies are counted and the part box is read.
        /// </summary>
        public static PrintPart ActivePart(ISldWorks sw, bool measure = true)
        {
            var model = sw?.ActiveDoc as IModelDoc2;
            if (model == null) return new PrintPart { Problem = "Open a part to print it." };
            var p = new PrintPart { Title = ActiveDocument.BareTitle(model), Path = model.GetPathName() ?? "" };
            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                p.Problem = "Open a part to print it (assemblies and drawings are not printed from here).";
                return p;
            }
            p.IsPart = true;
            if (p.Path.Length == 0) p.Problem = "Save the part first: the sliced file is kept beside it, in a print folder.";
            else if (model.GetSaveFlag()) p.Problem = "Save the part first, so the print matches the saved file.";
            if (!measure) return p;
            var part = (IPartDoc)model;
            // UNVERIFIED: GetBodies2(swSolidBody, visibleOnly) returns object[] of IBody2; null when none.
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            var solids = bodies?.Length ?? 0;
            p.ApproxSizeMm = ApproxBoundingBoxMm(part);
            if (p.Problem != null) return p;
            if (solids == 0) p.Problem = "The part has no solid body; only a closed solid can be printed.";
            else if (solids > 1) p.Problem = "The part has " + solids + " solid bodies. Print one body at a time: save each as its own part, or combine them.";
            return p;
        }

        /// <summary>
        /// Export the active part to <paramref name="stlPath"/> as a binary STL in millimetres, as a
        /// copy (the open document keeps its own path and saved state). Null on success, else why not.
        /// </summary>
        public static string ExportStl(ISldWorks sw, string stlPath)
        {
            var part = ActivePart(sw);
            if (part.Problem != null) return part.Problem;
            var model = (IModelDoc2)sw.ActiveDoc;
            Directory.CreateDirectory(Path.GetDirectoryName(stlPath));
            if (File.Exists(stlPath)) return "Refusing to overwrite " + stlPath + ".";

            // UNVERIFIED: swExportStlUnits takes a swLengthUnit_e value and sets the unit SaveAs3 writes
            // the STL in; swSTLBinaryFormat true writes binary STL. Both are system options (ISldWorks),
            // read first and restored below so the user's own STL settings are untouched.
            var units = (int)swUserPreferenceIntegerValue_e.swExportStlUnits;
            var binary = (int)swUserPreferenceToggle_e.swSTLBinaryFormat;
            var oldUnits = sw.GetUserPreferenceIntegerValue(units);
            var oldBinary = sw.GetUserPreferenceToggle(binary);
            try
            {
                sw.SetUserPreferenceIntegerValue(units, (int)swLengthUnit_e.swMM);
                sw.SetUserPreferenceToggle(binary, true);
                // UNVERIFIED: SaveAs3 with a .stl path and Silent | Copy writes the STL with the options
                // above and leaves the open part as itself (verified for .step/.pdf, not for .stl).
                var errors = 0;
                var warnings = 0;
                var saved = model.Extension.SaveAs3(stlPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy),
                    null, null, ref errors, ref warnings);
                if (!saved || errors != 0 || !File.Exists(stlPath))
                    return "SolidWorks did not write the STL (return " + saved + ", error bits " + errors + ", warning bits " + warnings + ").";
                return null;
            }
            finally
            {
                sw.SetUserPreferenceIntegerValue(units, oldUnits);
                sw.SetUserPreferenceToggle(binary, oldBinary);
            }
        }
    }
}
