// Independently hand-written, compile-time-only stand-ins authored from publicly documented SOLIDWORKS API interface/enum names and signatures. Not derived from, and not a replacement for, Dassault Systèmes' SolidWorks.Interop.* assemblies; no GUIDs, DispIds or runtime-functional values are present. SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks Corporation. This project is not affiliated with, sponsored by, or endorsed by Dassault Systèmes.
// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.

namespace SolidWorks.Interop.swpublished
{
    public interface ISwAddin
    {
        bool ConnectToSW(object thisSW, int cookie);
        bool DisconnectFromSW();
    }
}
