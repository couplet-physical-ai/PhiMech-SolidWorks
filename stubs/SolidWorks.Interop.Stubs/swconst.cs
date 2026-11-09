// Independently hand-written, compile-time-only stand-ins authored from publicly documented SOLIDWORKS API interface/enum names and signatures. Not derived from, and not a replacement for, Dassault Systèmes' SolidWorks.Interop.* assemblies; no GUIDs, DispIds or runtime-functional values are present. SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks Corporation. This project is not affiliated with, sponsored by, or endorsed by Dassault Systèmes.
// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.

namespace SolidWorks.Interop.swconst
{
    public enum swCommandItemType_e { swMenuItem = -1001, swToolbarItem = -1002 }
    public enum swDocumentTypes_e { swDocPART = -1003 }
    public enum swMessageBoxIcon_e { swMbWarning = -1004, swMbInformation = -1005, swMbStop = -1006 }
    public enum swMessageBoxBtn_e { swMbOk = -1007 }
    public enum swBodyType_e { swSolidBody = -1008 }
    public enum swSaveAsVersion_e { swSaveAsCurrentVersion = -1009 }
    public enum swSaveAsOptions_e { swSaveAsOptions_Silent = -1010, swSaveAsOptions_Copy = -1011 }
    public enum swUserPreferenceOption_e { swDetailingNoOptionSpecified = -1012 }
    public enum swLengthUnit_e { swMM = -1013 }
    public enum swUserPreferenceIntegerValue_e
    {
        swStepAP = -1014,
        swUnitsLinear = -1015,
        swExportStlUnits = -1016,
    }
    public enum swUserPreferenceToggle_e
    {
        swStepExportFaceEdgeProps = -1017,
        swSTLBinaryFormat = -1018,
    }
}
