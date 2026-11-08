// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Write the smallest valid PhiMech design-IR document that points at the exported STEP:
// project/title/rev/company/units plus ONE component whose source is
//   { "kind": "import", "path": "<step>", "scale": 1.0 }
// (the `import` variant of $defs/source in contracts/design-ir.schema.json).
//
// This is a hand-off, not a sync. PhiMech treats an import as a fixed envelope to design an
// enclosure around; the feature tree stays here, the parameters stay there. The add-in does not
// try to describe geometry in the IR because the IR has no vocabulary for a feature tree and
// should not grow one for this.

using System.IO;
using PhiMech.SolidWorks.Contract;
using SolidWorks.Interop.sldworks;

namespace PhiMech.SolidWorks.Commands
{
    public sealed class ExportIrStubCommand : ICommand
    {
        private readonly ISldWorks _sw;

        public ExportIrStubCommand(ISldWorks sw)
        {
            _sw = sw;
        }

        public CommandResult Execute()
        {
            var part = ActiveDocument.RequirePart(_sw, out var model, out var why);
            if (part == null)
            {
                return CommandResult.Fail(why);
            }

            var title = ActiveDocument.BareTitle(model);
            var slug = Slug.From(title);
            var dir = ActiveDocument.DefaultOutputDirectory(model);

            // Prefer the STEP this session exported; otherwise expect <slug>.step next to the part.
            var stepPath = ExportStepCommand.LastExportedPath;
            if (string.IsNullOrEmpty(stepPath) || !File.Exists(stepPath))
            {
                stepPath = Path.Combine(dir, slug + ".step");
            }
            if (!File.Exists(stepPath))
            {
                return CommandResult.Fail("No STEP to reference. Run 'Export STEP for PhiMech' first; expected "
                    + stepPath);
            }

            // Never point PhiMech at a file we would not accept ourselves.
            var readiness = StepReadiness.Read(stepPath);
            if (readiness.Verdict != Verdict.Ready)
            {
                return CommandResult.Fail("Refusing to write an IR that references a STEP outside the profile:\n\n"
                    + readiness.Summary());
            }

            var doc = Build(slug, title, stepPath, RevisionOf(model));
            var outPath = Path.Combine(dir, slug + ".design.json");
            File.WriteAllText(outPath, doc.ToJson());

            return CommandResult.Success("Wrote " + outPath + "\n\n" + doc.ToJson()
                + "\nNext, in PhiMech:\n"
                + "  bin/phimech ir " + slug + ".design.json          # validate\n"
                + "  bin/phimech interop risk " + Path.GetFileName(stepPath) + "   # edit-risk level\n"
                + "  # then add components/enclosure around this envelope and `synth build`.");
        }

        /// <summary>Pure function so tests can validate the output against the pinned schema without SolidWorks.</summary>
        public static IrDocument Build(string slug, string title, string stepPath, string rev)
        {
            return new IrDocument
            {
                Project = slug,
                Title = title,
                Rev = string.IsNullOrWhiteSpace(rev) ? "A" : rev,
                Company = "generic",
                Components =
                {
                    new IrComponent
                    {
                        Ref = "SW1",
                        Name = title,
                        Source = new IrImportSource
                        {
                            // Forward slashes: valid on Windows, and PhiMech runs on Linux/macOS.
                            Path = stepPath.Replace('\\', '/'),
                            Scale = 1.0,
                        },
                    },
                },
            };
        }

        private static string RevisionOf(IModelDoc2 model)
        {
            // UNVERIFIED: CustomPropertyManager lookup of a "Revision" property on the default
            // configuration ("" = document-level). Get6 returns resolved text in the out param.
            try
            {
                var mgr = model.Extension.get_CustomPropertyManager("");
                string valOut, resolved;
                bool wasResolved, linkToProperty;
                mgr.Get6("Revision", false, out valOut, out resolved, out wasResolved, out linkToProperty);
                return string.IsNullOrWhiteSpace(resolved) ? "A" : resolved.Trim();
            }
            catch
            {
                return "A";
            }
        }
    }
}
