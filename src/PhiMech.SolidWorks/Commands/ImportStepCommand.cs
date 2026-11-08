// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Import a STEP (typically written by PhiMech) and run the readiness assertions from
// contracts/step-profile.md: millimetres, exactly one solid body, warn above 1000 faces. The
// text-level checks run *before* the import so an inch file or a triangle soup is refused
// before SolidWorks spends a minute on it; the body-count check runs after, against what the
// kernel actually produced. The kernel body count still needs verification on a licensed seat.

using System;
using System.IO;
using PhiMech.SolidWorks.Contract;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace PhiMech.SolidWorks.Commands
{
    public sealed class ImportStepCommand : ICommand
    {
        private readonly ISldWorks _sw;

        public ImportStepCommand(ISldWorks sw)
        {
            _sw = sw;
        }

        public CommandResult Execute()
        {
            var path = PromptForStepFile();
            if (string.IsNullOrEmpty(path))
            {
                return CommandResult.Fail("No file chosen.");
            }
            if (!File.Exists(path))
            {
                return CommandResult.Fail("File not found: " + path);
            }

            // 1-4: file-level assertions. A hard failure here means we never open the file.
            var readiness = StepReadiness.Read(path);
            if (readiness.Verdict != Verdict.Ready)
            {
                return CommandResult.Fail("Not imported. " + path + " does not meet the PhiMech STEP profile:\n\n"
                    + readiness.Summary());
            }

            // 5: let SolidWorks import it and count what came out.
            // UNVERIFIED: LoadFile4 with import data is the documented route for STEP; it returns
            // the new IModelDoc2 (a part for a single-solid STEP). Errors are swFileLoadError_e bits.
            // Import options are left at defaults on purpose: the point of the check is to see
            // what a default import produces, because that is what an engineer double-clicking
            // the file gets. UNVERIFIED: GetImportFileData returns an ImportStepData for .step.
            var importData = (ImportStepData)_sw.GetImportFileData(path);
            var errors = 0;
            var model = _sw.LoadFile4(path, "r", importData, ref errors) as IModelDoc2;
            if (model == null)
            {
                return CommandResult.Fail("SolidWorks could not open " + path + " (error bits " + errors + ").\n\n"
                    + readiness.Summary());
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                // A multi-solid STEP may become an assembly depending on import options.
                readiness.RecordImportDiagnostics("SolidWorks opened the file as an assembly");
                return CommandResult.Fail("SolidWorks opened " + path + " as an assembly, not a part; the file "
                    + "declared " + readiness.Solids + " solid(s). PhiMech hands off one part per STEP.\n\n"
                    + readiness.Summary());
            }

            var part = (IPartDoc)model;
            // UNVERIFIED: GetBodies2(swSolidBody, visibleOnly=false) — count every solid, hidden or not.
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            readiness.RecordImportedBodies(bodies?.Length ?? 0);

            // 6: topology size from the kernel (the text scan already counted entities; the kernel
            // count is the one that matters for edit performance).
            var faceCount = 0;
            if (bodies != null)
            {
                foreach (var b in bodies)
                {
                    var body = b as IBody2;
                    if (body != null)
                    {
                        faceCount += body.GetFaceCount();  // UNVERIFIED: IBody2.GetFaceCount exists and is cheap.
                    }
                }
            }
            if (faceCount > StepReadiness.WarnFaces)
            {
                readiness.RecordImportDiagnostics("kernel reports " + faceCount + " faces after import (> "
                    + StepReadiness.WarnFaces + ")");
            }

            // 7: import diagnostics. TODO: SolidWorks surfaces these through the Import Diagnostics
            // feature (IFeatureManager / swImportDiagnostics); there is no single documented API call
            // that returns the list. Record the file-load error bits for now.
            if (errors != 0)
            {
                readiness.RecordImportDiagnostics("swFileLoadError_e bits: " + errors);
            }

            var verdict = readiness.Verdict == Verdict.Ready
                ? "Imported as one solid body in millimetres."
                : "Imported, but the result does NOT meet the profile.";
            return new CommandResult(readiness.Verdict == Verdict.Ready,
                verdict + "\n\n" + readiness.Summary()
                + "\nRemember: this is a dumb solid. There is no feature tree and no parameters; use "
                + "Recognize Features if you need to edit it, or change the design in PhiMech and re-export.");
        }

        private string PromptForStepFile()
        {
            // UNVERIFIED: ISldWorks.GetOpenFileName signature (title, initialName, filter,
            // out fileOptions, out configName, out displayName). Returns "" when cancelled.
            var fileOptions = 0;
            var configName = "";
            var displayName = "";
            return _sw.GetOpenFileName(
                "Import STEP from PhiMech",
                "",
                "STEP files (*.step;*.stp)|*.step;*.stp|All files (*.*)|*.*||",
                out fileOptions, out configName, out displayName);
        }
    }
}
