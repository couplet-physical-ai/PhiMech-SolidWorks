// Independently hand-written, compile-time-only stand-ins authored from publicly documented SOLIDWORKS API interface/enum names and signatures. Not derived from, and not a replacement for, Dassault Systèmes' SolidWorks.Interop.* assemblies; no GUIDs, DispIds or runtime-functional values are present. SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks Corporation. This project is not affiliated with, sponsored by, or endorsed by Dassault Systèmes.
// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.

namespace SolidWorks.Interop.sldworks
{
    public interface ISldWorks
    {
        object ActiveDoc { get; }
        bool SetAddinCallbackInfo2(int cookie, object addin, int addinCookie);
        ICommandManager GetCommandManager(int cookie);
        int SendMsgToUser2(string message, int icon, int buttons);
        bool SetUserPreferenceIntegerValue(int userPreference, int value);
        bool SetUserPreferenceToggle(int userPreference, bool onFlag);
        int GetUserPreferenceIntegerValue(int userPreference);
        bool GetUserPreferenceToggle(int userPreference);
        object GetImportFileData(string fileName);
        object LoadFile4(string fileName, string argString, object importData, ref int errors);
        string GetOpenFileName(string dialogTitle, string initialFileName, string fileFilter,
            out int openOptions, out string configName, out string displayName);
        ITaskpaneView CreateTaskpaneView3(object imageList, string toolTip);
    }

    public interface ICommandManager
    {
        ICommandGroup CreateCommandGroup2(int userId, string title, string toolTip, string hint,
            int position, bool ignorePreviousVersion, ref int errors);
        bool RemoveCommandGroup2(int userId, bool runtimeOnly);
    }

    public interface ICommandGroup
    {
        object IconList { get; set; }
        object MainIconList { get; set; }
        bool HasToolbar { get; set; }
        bool HasMenu { get; set; }
        int AddCommandItem2(string name, int position, string hintString, string toolTip, int imageListIndex,
            string callbackFunction, string enableMethod, int userId, int menuTbOption);
        bool Activate();
    }

    public interface ITaskpaneView
    {
        bool DisplayWindowFromHandlex64(long windowHandle);
        bool DeleteView();
    }

    public interface IModelDoc2
    {
        int GetType();
        string GetPathName();
        string GetTitle();
        bool GetSaveFlag();
        IModelDocExtension Extension { get; }
    }

    public interface IModelDocExtension
    {
        bool SaveAs3(string name, int version, int options, object exportData, object advancedSaveAsOptions,
            ref int errors, ref int warnings);
        int SetUserPreferenceInteger(int userPreference, int userPreferenceOption, int value);
        ICustomPropertyManager get_CustomPropertyManager(string configuration);
    }

    public interface ICustomPropertyManager
    {
        int Get6(string fieldName, bool useCached, out string valOut, out string resolvedValOut,
            out bool wasResolved, out bool linkToProperty);
    }

    public interface IPartDoc
    {
        object GetBodies2(int bodyType, bool visibleOnly);
        object GetPartBox(bool useBoundingBoxSolidOnly);
    }

    public interface IBody2
    {
        int GetFaceCount();
    }

    public class ImportStepData { }
}
