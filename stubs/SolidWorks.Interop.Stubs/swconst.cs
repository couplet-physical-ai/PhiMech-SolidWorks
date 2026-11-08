// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
// COMPILE-ONLY names from public SolidWorks API documentation; ALL numeric values below are placeholders.
// Use licensed vendor interop DLLs, not these values, for any real SolidWorks build.

namespace SolidWorks.Interop.swconst
{
    public enum swCommandItemType_e { swMenuItem = 1, swToolbarItem = 2 }
    public enum swDocumentTypes_e { swDocPART = 1 }
    public enum swMessageBoxIcon_e { swMbWarning = 1, swMbInformation = 2, swMbStop = 4 }
    public enum swMessageBoxBtn_e { swMbOk = 2 }
    public enum swBodyType_e { swSolidBody = 0 }
    public enum swSaveAsVersion_e { swSaveAsCurrentVersion = 0 }
    public enum swSaveAsOptions_e { swSaveAsOptions_Silent = 1, swSaveAsOptions_Copy = 2 }
    public enum swUserPreferenceOption_e { swDetailingNoOptionSpecified = 0 }
    public enum swLengthUnit_e { swMM = 0 }
    public enum swUserPreferenceIntegerValue_e
    {
        swStepAP = 214000,
        swUnitsLinear = 214001,
        swExportStlUnits = 214004,
    }
    public enum swUserPreferenceToggle_e
    {
        swStepExportFaceEdgeProps = 214100,
        swSTLBinaryFormat = 214101,
    }
}
