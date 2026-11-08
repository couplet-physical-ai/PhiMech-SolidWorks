// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.

using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace PhiMech.SolidWorks.Commands
{
    /// <summary>What a toolbar click produces: a verdict and text the user sees in a message box.</summary>
    public sealed class CommandResult
    {
        public CommandResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public bool Ok { get; }
        public string Message { get; }

        public static CommandResult Fail(string message) => new CommandResult(false, message);
        public static CommandResult Success(string message) => new CommandResult(true, message);
    }

    /// <summary>
    /// A user-initiated action. Nothing implements Execute() in a way that runs without a click,
    /// and nothing here writes a .sldprt: the add-in reads documents and asks SolidWorks to write
    /// STEP, or writes JSON of its own.
    /// </summary>
    public interface ICommand
    {
        CommandResult Execute();
    }

    /// <summary>Helpers shared by the commands.</summary>
    internal static class ActiveDocument
    {
        /// <summary>The active document if it is a part; otherwise null with a reason in <paramref name="why"/>.</summary>
        public static IPartDoc RequirePart(ISldWorks sw, out IModelDoc2 model, out string why)
        {
            model = sw.ActiveDoc as IModelDoc2;
            if (model == null)
            {
                why = "No document is open.";
                return null;
            }
            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                why = "The active document is not a part. PhiMech exchanges one STEP per part and has no "
                    + "assembly STEP; open the part you want to hand off.";
                return null;
            }
            why = null;
            return (IPartDoc)model;
        }

        /// <summary>Directory the model was saved in, or the user's Documents folder for an unsaved part.</summary>
        public static string DefaultOutputDirectory(IModelDoc2 model)
        {
            var path = model.GetPathName();
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetDirectoryName(path);
            }
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Title with the extension SolidWorks appends for saved docs removed.</summary>
        public static string BareTitle(IModelDoc2 model)
        {
            var title = model.GetTitle() ?? "untitled";
            var dot = title.LastIndexOf('.');
            return dot > 0 && title.Substring(dot).StartsWith(".sld", System.StringComparison.OrdinalIgnoreCase)
                ? title.Substring(0, dot)
                : title;
        }
    }
}
