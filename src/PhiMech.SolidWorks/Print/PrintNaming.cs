// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// File names for sliced parts: <part>-<material>-<sha8>.gcode.3mf, where sha8 is the first eight
// hex digits of the SHA-256 of the .gcode.3mf itself. Deterministic, so the same bytes always get
// the same name; content-addressed, so a different slice can never quietly take an old name on
// the printer (and an existing name is only ever accepted when the bytes match).

using System;
using System.Text.RegularExpressions;
using PhiMech.SolidWorks.Contract;

namespace PhiMech.SolidWorks.Print
{
    public static class PrintNaming
    {
        public const string Extension = ".gcode.3mf";
        private const int MaxPartChars = 60;
        private static readonly Regex Hex = new Regex("^[0-9a-fA-F]{8,}$", RegexOptions.CultureInvariant);
        private static readonly Regex Safe = new Regex(@"^[a-z0-9][a-z0-9-]{0,100}\.gcode\.3mf$", RegexOptions.CultureInvariant);

        /// <param name="part">the part's title ("Bracket.SLDPRT", "Bracket")</param>
        /// <param name="material">PETG / PLA</param>
        /// <param name="sha256">hex SHA-256 of the .gcode.3mf (at least 8 digits)</param>
        public static string FileName(string part, string material, string sha256)
        {
            if (sha256 == null || !Hex.IsMatch(sha256)) throw new ArgumentException("expected a hex SHA-256", nameof(sha256));
            var p = Slug.From(StripSolidWorksExtension(part));
            if (p.Length > MaxPartChars) p = p.Substring(0, MaxPartChars).TrimEnd('-');
            var m = Slug.From(string.IsNullOrWhiteSpace(material) ? "material" : material);
            return p + "-" + m + "-" + sha256.Substring(0, 8).ToLowerInvariant() + Extension;
        }

        /// <summary>A name the add-in will send to the printer: lower-case slug, no path, the .gcode.3mf extension.</summary>
        public static bool IsSafeRemoteName(string name) => name != null && Safe.IsMatch(name);

        private static string StripSolidWorksExtension(string title)
        {
            var t = (title ?? "").Trim();
            var dot = t.LastIndexOf('.');
            return dot > 0 && t.Substring(dot).StartsWith(".sld", StringComparison.OrdinalIgnoreCase) ? t.Substring(0, dot) : t;
        }
    }
}
