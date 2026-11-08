// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Export the active part as STEP AP214 in millimetres, then re-read the file and refuse to call
// it a success unless it passes the contract profile (contracts/step-profile.md). "It saved" is
// not the bar; "PhiMech's `interop step` would say READY and not FACETED" is.

using System;
using System.IO;
using PhiMech.SolidWorks.Contract;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace PhiMech.SolidWorks.Commands
{
    public sealed class ExportStepCommand : ICommand
    {
        private readonly ISldWorks _sw;

        public ExportStepCommand(ISldWorks sw)
        {
            _sw = sw;
        }

        /// <summary>Where the last successful export went, so the IR stub can point at it.</summary>
        public static string LastExportedPath { get; private set; }

        public CommandResult Execute()
        {
            var part = ActiveDocument.RequirePart(_sw, out var model, out var why);
            if (part == null)
            {
                return CommandResult.Fail(why);
            }

            // One solid body per file (profile rule 5). Refuse rather than silently write several.
            // UNVERIFIED: GetBodies2 returns object[] of IBody2 for swSolidBody; null when none.
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            var solidCount = bodies?.Length ?? 0;
            if (solidCount == 0)
            {
                return CommandResult.Fail("The part has no solid body; PhiMech needs a closed solid, not surfaces.");
            }
            if (solidCount > 1)
            {
                return CommandResult.Fail("The part has " + solidCount + " solid bodies. Export one body per STEP: "
                    + "save each as its own part, or combine them. An IR `import` source is one envelope.");
            }

            var slug = Slug.From(ActiveDocument.BareTitle(model));
            var outPath = Path.Combine(ActiveDocument.DefaultOutputDirectory(model), slug + ".step");

            ApplyExportPreferences();

            // UNVERIFIED: SaveAs3 with swSaveAsOptions_Silent writes to the given extension using
            // the STEP translator and the user preferences set above. Errors/warnings come back
            // as swFileSaveError_e / swFileSaveWarning_e bit fields.
            var errors = 0;
            var warnings = 0;
            var saved = model.Extension.SaveAs3(
                outPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null, null, ref errors, ref warnings);

            if (!saved)
            {
                return CommandResult.Fail("SolidWorks refused to write " + outPath
                    + " (error bits " + errors + ", warning bits " + warnings + ").");
            }

            // Now check what actually landed on disk. This is the half that does not depend on
            // the SolidWorks API being what the documentation says.
            var readiness = StepReadiness.Read(outPath);
            if (readiness.Verdict != Verdict.Ready)
            {
                return CommandResult.Fail("Wrote " + outPath + " but it does NOT meet the PhiMech STEP profile:\n\n"
                    + readiness.Summary()
                    + "\nThe file was left in place for inspection. Nothing else was written.");
            }

            LastExportedPath = outPath;
            return CommandResult.Success("Exported " + outPath + "\n\n" + readiness.Summary()
                + (warnings != 0 ? "\nSolidWorks reported warning bits " + warnings + " during save." : "")
                + "\nNext: 'Write design-IR stub' to reference this file from a PhiMech design.");
        }

        /// <summary>
        /// Force the STEP translator settings the profile requires. These are process-wide user
        /// preferences in SolidWorks, so the previous values are restored afterwards where possible.
        /// TODO: restore previous values; v0 sets and leaves them.
        /// </summary>
        private void ApplyExportPreferences()
        {
            // UNVERIFIED: swStepAP takes 203 or 214 in shipping versions; 242 appeared in 2021+.
            // The preference name is documented under swUserPreferenceIntegerValue_e.
            _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, 214);

            // UNVERIFIED: swStepExportFaceEdgeProps keeps colour/face properties (fine under AP214).
            _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportFaceEdgeProps, true);

            // UNVERIFIED: which preference selects "solid/surface geometry" vs "wireframe"; the
            // SolidWorks UI exposes it under Export Options > STEP > Output as. The enum member is
            // swStepExportPreference per the public SOLIDWORKS API Help (0 = solid/surface assumed). Confirm on a seat and pin it here.
            // _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepExportPreference, 0);

            // Units: STEP always states its unit, and SolidWorks writes the document's unit. Make
            // the document millimetres so the file says SI_UNIT(.MILLI.,.METRE.). This edits a
            // document setting, which the user asked for by clicking Export.
            // UNVERIFIED: swUnitsLinear / swMM enum names and that they drive the STEP unit.
            var model = (IModelDoc2)_sw.ActiveDoc;
            model.Extension.SetUserPreferenceInteger(
                (int)swUserPreferenceIntegerValue_e.swUnitsLinear,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                (int)swLengthUnit_e.swMM);
        }
    }
}
