# PhiMech SolidWorks

A Windows .NET Framework add-in for STEP AP214/design-IR exchange and an optional, user-configured local 3D-print workflow.

![Status: experimental](https://img.shields.io/badge/status-experimental-orange) ![License: Apache--2.0](https://img.shields.io/badge/license-Apache--2.0-blue)

Created at [Exowatt](https://exowatt.com). This source extraction is vendor-neutral and needs no Exowatt services.

## Status

Experimental: **build unverified locally; CI is the check.** This machine has no `dotnet`; the Windows workflow builds against compile-only stubs and runs seat-free smoke tests. No licensed-seat or hardware validation has been performed. Do not deploy an assembly built with stubs. CAD drafting/review/approval, simulation, and the private job-server protocol are **not included in this release**.

## Prerequisites

Windows x64 with a licensed SolidWorks installation for running the add-in. Obtain the SolidWorks API interop assemblies (`SolidWorks.Interop.sldworks.dll`, `swconst.dll`, `swpublished.dll`) from your own licensed installation's `api\redist` directory via `SolidWorksApiDir`; real Dassault DLLs are never committed to this repository. Use the .NET SDK 8+ and .NET Framework 4.8 targeting pack to build. For optional printing, install/configure OrcaSlicer and a compatible local PhiMech CLI separately.

## Install / build

Compile-only check with the hand-written stubs (Windows; no SolidWorks seat needed):

```powershell
dotnet build src/PhiMech.SolidWorks/PhiMech.SolidWorks.csproj
dotnet test tests/PhiMech.SolidWorks.Tests/PhiMech.SolidWorks.Tests.csproj
```

For a runtime build, delete `stubs/` and set `SolidWorksApiDir` to build against your installed interop assemblies:

```powershell
dotnet build src/PhiMech.SolidWorks/PhiMech.SolidWorks.csproj -p:SolidWorksApiDir='C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
```

The separate `stubs/` directory provides a compile-only fallback when `SolidWorksApiDir` is unset; delete it for a runtime build. Neither build validates add-in behavior; test COM registration and all runtime interactions on a licensed seat before use.

## Usage / quickstart

In SolidWorks, register the real-interop assembly by your vendor-documented COM-add-in procedure. Use the toolbar to export/import STEP or write a design-IR stub for a saved part. These features never invoke drawing or Simulation. Open the task pane for optional printer status, fit, slice and upload; no print is started automatically.

## Configuration

- `SolidWorksApiDir`: directory of user-supplied vendor interop DLLs; when unset and `stubs/` exists, compile-only stubs are used instead.
- `PHIMECH_PRINTER_URL`: optional Moonraker URL. Default is `http://127.0.0.1:7125`; set your printer's own LAN address to use a remote device.
- `%LOCALAPPDATA%\PhiMech\print-settings.json`: optional `printer_url`, `envelope_mm` (three positive X/Y/Z millimetre values), and `profile_directory` (local OrcaSlicer profiles). An example envelope of `[200, 200, 200]` is **not a real printer specification**; check your printer to read its actual volume. Use your own slicer profile and machine settings, not a baked-in printer profile.
- `PHIMECH_CMD`: optional path to a separately installed PhiMech-compatible CLI (`print slice`/`print status`). `PHIMECH_ORCASLICER` and `PHIMECH_ORCA_PROFILES` select its local slicer executable and profile directory. If absent, slicing is disabled; fit and printer checks remain available. The external CLI's selected machine/profile needs independent verification before printing.

## Stub provenance

`stubs/SolidWorks.Interop.Stubs/` holds **three independently hand-written, compile-only stand-ins** (`sldworks.cs`, `swconst.cs`, `swpublished.cs`) based only on publicly documented interface/enum names and signatures. They provide only the members this add-in uses: interface signatures and placeholder enum names/values, **not vendor constants**, documentation prose, or implementations. See [stub notes](stubs/SolidWorks.Interop.Stubs/README.md). For a real build, use your own licensed SOLIDWORKS installation's `api\redist` interop DLLs through `SolidWorksApiDir`; the real Dassault DLLs are never committed. Delete `stubs/` to avoid accidentally using them in a runtime build. These stand-ins warrant vendor-license/legal review before any public release.

## License

Original source is licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE). Newtonsoft.Json is a separate MIT NuGet dependency. SOLIDWORKS and its SDK are proprietary products under their vendor's terms. SOLIDWORKS is a trademark of Dassault Systèmes. This project is not affiliated with or endorsed by Dassault Systèmes.

## Contributing

See [CONTRIBUTING](https://github.com/couplet-physical-ai/.github/blob/main/CONTRIBUTING.md). Contributions require DCO sign-off (`git commit -s`); no CLA is required.

## Security

Report vulnerabilities privately via [GitHub private vulnerability reporting](https://github.com/couplet-physical-ai/PhiMech-SolidWorks/security/advisories/new), or contact software@exowatt.com. See the [security policy](https://github.com/couplet-physical-ai/.github/blob/main/SECURITY.md); do not open a public issue.
