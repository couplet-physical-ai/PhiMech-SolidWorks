// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
using System.Runtime.InteropServices;

// RegAsm must expose only AddIn, which explicitly opts in. Public UI enums and helpers are
// managed implementation details, not independently registered COM types.
[assembly: ComVisible(false)]
