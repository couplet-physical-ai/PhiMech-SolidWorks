# Compile-only SOLIDWORKS interop stubs

These three files are hand-written from the public SOLIDWORKS API Help. They contain signatures only, placeholder enum constants (not vendor values), and only the members used by this add-in ([member-to-source map](USAGE.md)). They cannot run the add-in or replace the proprietary interop assemblies. Delete `stubs/` and set `SolidWorksApiDir` to build against the interop assemblies from your licensed installation; verify signatures and behavior on a licensed seat.
