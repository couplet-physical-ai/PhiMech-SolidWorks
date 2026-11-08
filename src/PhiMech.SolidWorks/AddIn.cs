// Licensed under Apache-2.0; copyright holder in NOTICE.
// Reduced COM add-in: STEP/design-IR exchange and optional local printing only.
// No operation touches a document until the user explicitly clicks a command.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PhiMech.SolidWorks.Commands;
using PhiMech.SolidWorks.UI;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace PhiMech.SolidWorks
{
    [ComVisible(true)]
    [Guid(AddInGuid)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    [ProgId("PhiMech.SolidWorks.AddIn")]
    public sealed class AddIn : ISwAddin
    {
        public const string AddInGuid = "9c4c2f7e-6b3e-4a52-9d6b-3f0e7a1c2d55";
        public const string Title = "PhiMech";
        public const string Description = "STEP AP214 and design-IR exchange with optional local printing";
        private const int CmdGroupId = 1;
        private const int CmdExportStep = 0;
        private const int CmdImportStep = 1;
        private const int CmdExportIrStub = 2;
        private ISldWorks _sw;
        private ICommandManager _cmdMgr;
        private ITaskpaneView _pane;
        private PrintPanel _print;
        private readonly Dictionary<int, ICommand> _commands = new Dictionary<int, ICommand>();

        public bool ConnectToSW(object thisSW, int cookie)
        {
            try
            {
                _sw = (ISldWorks)thisSW;
                _sw.SetAddinCallbackInfo2(0, this, cookie);
                _cmdMgr = _sw.GetCommandManager(cookie);
                _commands[CmdExportStep] = new ExportStepCommand(_sw);
                _commands[CmdImportStep] = new ImportStepCommand(_sw);
                _commands[CmdExportIrStub] = new ExportIrStubCommand(_sw);
                var errors = 0;
                var group = _cmdMgr.CreateCommandGroup2(CmdGroupId, Title, Description, Description, -1, true, ref errors);
                group.IconList = IconPaths("toolbar");
                group.MainIconList = IconPaths("main");
                const int menuAndToolbar = (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem;
                group.AddCommandItem2("Export STEP", -1, "Export active part as STEP AP214", "Export STEP", 0,
                    nameof(OnExportStep), nameof(EnableForPart), CmdExportStep, menuAndToolbar);
                group.AddCommandItem2("Import STEP", -1, "Open a STEP part", "Import STEP", 1,
                    nameof(OnImportStep), nameof(EnableAlways), CmdImportStep, menuAndToolbar);
                group.AddCommandItem2("Write design-IR stub", -1, "Write a minimal design-IR JSON", "IR stub", 2,
                    nameof(OnExportIrStub), nameof(EnableForPart), CmdExportIrStub, menuAndToolbar);
                group.HasToolbar = true;
                group.HasMenu = true;
                group.Activate();
                _pane = _sw.CreateTaskpaneView3(IconPaths("main"), Title);
                if (_pane != null)
                {
                    _print = new PrintPanel(new Print.PrintFlow(Print.PrintSettings.Load()),
                        measure => PrintExport.ActivePart(_sw, measure),
                        stl => PrintExport.ExportStl(_sw, stl));
                    _pane.DisplayWindowFromHandlex64(_print.Handle.ToInt64());
                }
                return true;
            }
            catch (Exception)
            {
                // A failed add-in load must not interfere with the host.
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            if (_pane != null)
            {
                _pane.DeleteView();
                Marshal.ReleaseComObject(_pane);
                _pane = null;
            }
            _print?.Dispose();
            _print = null;
            if (_cmdMgr != null)
            {
                _cmdMgr.RemoveCommandGroup2(CmdGroupId, false);
                _cmdMgr = null;
            }
            _commands.Clear();
            if (_sw != null)
            {
                Marshal.ReleaseComObject(_sw);
                _sw = null;
            }
            return true;
        }

        public void OnExportStep() => Run(CmdExportStep);
        public void OnImportStep() => Run(CmdImportStep);
        public void OnExportIrStub() => Run(CmdExportIrStub);
        public int EnableForPart()
        {
            var doc = _sw?.ActiveDoc as IModelDoc2;
            return doc != null && doc.GetType() == (int)swDocumentTypes_e.swDocPART ? 1 : 0;
        }
        public int EnableAlways() => 1;

        private void Run(int id)
        {
            if (!_commands.TryGetValue(id, out var command)) return;
            try
            {
                var result = command.Execute();
                _sw.SendMsgToUser2(result.Message,
                    (int)(result.Ok ? swMessageBoxIcon_e.swMbInformation : swMessageBoxIcon_e.swMbWarning),
                    (int)swMessageBoxBtn_e.swMbOk);
                _print?.RefreshContext();
            }
            catch (Exception ex)
            {
                _sw.SendMsgToUser2(Title + ": " + ex.GetType().Name + ": " + ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        private static string[] IconPaths(string prefix)
        {
            var dir = Path.Combine(Path.GetDirectoryName(typeof(AddIn).Assembly.Location) ?? "", "Resources", "icons");
            return new[] { 20, 32, 40, 64, 96, 128 }.Select(n => Path.Combine(dir, prefix + n + ".png")).ToArray();
        }

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            const string keyPath = @"SOFTWARE\SolidWorks\AddIns\{" + AddInGuid + "}";
            using (var key = Registry.LocalMachine.CreateSubKey(keyPath))
            {
                if (key == null) throw new InvalidOperationException("could not create " + keyPath + " (run elevated)");
                key.SetValue(null, 0, RegistryValueKind.DWord);
                key.SetValue("Title", Title, RegistryValueKind.String);
                key.SetValue("Description", Description, RegistryValueKind.String);
            }
            const string startupPath = @"SOFTWARE\SolidWorks\AddInsStartup\{" + AddInGuid + "}";
            using (var key = Registry.CurrentUser.CreateSubKey(startupPath))
                key?.SetValue(null, 0, RegistryValueKind.DWord);
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\AddIns\{" + AddInGuid + "}", false);
            Registry.CurrentUser.DeleteSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\{" + AddInGuid + "}", false);
        }
    }
}
