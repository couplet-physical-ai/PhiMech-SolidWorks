// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
// COMPILE-ONLY stubs. No behaviour. See README.md in this directory.

namespace SolidWorks.Interop.swpublished
{
    /// <summary>The interface SolidWorks calls to load and unload an add-in.</summary>
    public interface ISwAddin
    {
        bool ConnectToSW(object thisSW, int cookie);
        bool DisconnectFromSW();
    }
}
