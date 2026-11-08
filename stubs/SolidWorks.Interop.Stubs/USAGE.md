# Stub coverage against the exported add-in

Each entry maps one kept stub member to its use in `src/PhiMech.SolidWorks/`; line numbers are from the 2026-11-08 export. The check is static and does **not** validate signatures against licensed interop assemblies.

## `sldworks.cs`

- `ISldWorks.ActiveDoc` → AddIn.cs:107; Commands/ICommand.cs:41; Commands/ExportStepCommand.cs:110; Commands/PrintExport.cs:56,87.
- `ISldWorks.SetAddinCallbackInfo2` → AddIn.cs:42.
- `ISldWorks.GetCommandManager` → AddIn.cs:43.
- `ISldWorks.SendMsgToUser2` → AddIn.cs:118,125.
- `ISldWorks.SetUserPreferenceIntegerValue` → Commands/ExportStepCommand.cs:96; Commands/PrintExport.cs:100,115.
- `ISldWorks.SetUserPreferenceToggle` → Commands/ExportStepCommand.cs:99; Commands/PrintExport.cs:101,116.
- `ISldWorks.GetUserPreferenceIntegerValue` → Commands/PrintExport.cs:96.
- `ISldWorks.GetUserPreferenceToggle` → Commands/PrintExport.cs:97.
- `ISldWorks.GetImportFileData` → Commands/ImportStepCommand.cs:52.
- `ISldWorks.LoadFile4` → Commands/ImportStepCommand.cs:54.
- `ISldWorks.GetOpenFileName` → Commands/ImportStepCommand.cs:119.
- `ISldWorks.CreateTaskpaneView3` → AddIn.cs:61.
- `ICommandManager.CreateCommandGroup2` → AddIn.cs:48.
- `ICommandManager.RemoveCommandGroup2` → AddIn.cs:90.
- `ICommandGroup.IconList` → AddIn.cs:49.
- `ICommandGroup.MainIconList` → AddIn.cs:50.
- `ICommandGroup.HasToolbar` → AddIn.cs:58.
- `ICommandGroup.HasMenu` → AddIn.cs:59.
- `ICommandGroup.AddCommandItem2` → AddIn.cs:52,54,56.
- `ICommandGroup.Activate` → AddIn.cs:60.
- `ITaskpaneView.DisplayWindowFromHandlex64` → AddIn.cs:67.
- `ITaskpaneView.DeleteView` → AddIn.cs:82.
- `IModelDoc2.GetType` → AddIn.cs:108; Commands/ICommand.cs:47; Commands/ImportStepCommand.cs:61; Commands/PrintExport.cs:59.
- `IModelDoc2.GetPathName` → Commands/ICommand.cs:60; Commands/PrintExport.cs:58.
- `IModelDoc2.GetTitle` → Commands/ICommand.cs:71.
- `IModelDoc2.GetSaveFlag` → Commands/PrintExport.cs:66.
- `IModelDoc2.Extension` → Commands/ExportIrStubCommand.cs:103; Commands/ExportStepCommand.cs:59,111; Commands/PrintExport.cs:106.
- `IModelDocExtension.SaveAs3` → Commands/ExportStepCommand.cs:59; Commands/PrintExport.cs:106.
- `IModelDocExtension.SetUserPreferenceInteger` → Commands/ExportStepCommand.cs:111.
- `IModelDocExtension.get_CustomPropertyManager` → Commands/ExportIrStubCommand.cs:103.
- `ICustomPropertyManager.Get6` → Commands/ExportIrStubCommand.cs:106.
- `IPartDoc.GetBodies2` → Commands/ExportStepCommand.cs:37; Commands/ImportStepCommand.cs:72; Commands/PrintExport.cs:70.
- `IPartDoc.GetPartBox` → Commands/PrintExport.cs:37.
- `IBody2.GetFaceCount` → Commands/ImportStepCommand.cs:85.
- `ImportStepData` (type; no members) → Commands/ImportStepCommand.cs:52.

`ICommandGroup` is returned by `ICommandManager.CreateCommandGroup2`; `IModelDocExtension` is the type of `IModelDoc2.Extension`; `ICustomPropertyManager` is returned by `get_CustomPropertyManager`. These types are kept structurally, even though their names do not occur in `src/`.

## `swconst.cs`

- `swCommandItemType_e.swMenuItem`, `swToolbarItem` → AddIn.cs:51.
- `swDocumentTypes_e.swDocPART` → AddIn.cs:108; Commands/ICommand.cs:47; Commands/ImportStepCommand.cs:61; Commands/PrintExport.cs:59.
- `swMessageBoxIcon_e.swMbInformation`, `swMbWarning` → AddIn.cs:119; `swMbStop` → AddIn.cs:126.
- `swMessageBoxBtn_e.swMbOk` → AddIn.cs:120,126.
- `swBodyType_e.swSolidBody` → Commands/ExportStepCommand.cs:37; Commands/ImportStepCommand.cs:72; Commands/PrintExport.cs:70.
- `swSaveAsVersion_e.swSaveAsCurrentVersion` → Commands/ExportStepCommand.cs:61; Commands/PrintExport.cs:106.
- `swSaveAsOptions_e.swSaveAsOptions_Silent` → Commands/ExportStepCommand.cs:62; Commands/PrintExport.cs:107; `swSaveAsOptions_Copy` → Commands/PrintExport.cs:107.
- `swUserPreferenceOption_e.swDetailingNoOptionSpecified` → Commands/ExportStepCommand.cs:113 (API's exact member name, not a drawing feature).
- `swLengthUnit_e.swMM` → Commands/ExportStepCommand.cs:114; Commands/PrintExport.cs:100.
- `swUserPreferenceIntegerValue_e.swStepAP` → Commands/ExportStepCommand.cs:96; `swUnitsLinear` → Commands/ExportStepCommand.cs:112; `swExportStlUnits` → Commands/PrintExport.cs:94.
- `swUserPreferenceToggle_e.swStepExportFaceEdgeProps` → Commands/ExportStepCommand.cs:99; `swSTLBinaryFormat` → Commands/PrintExport.cs:95.

## `swpublished.cs`

- `ISwAddin.ConnectToSW` → AddIn.cs:37 (implementation).
- `ISwAddin.DisconnectFromSW` → AddIn.cs:78 (implementation).
