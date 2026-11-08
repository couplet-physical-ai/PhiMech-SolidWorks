// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The one design system every PhiMech surface draws from (panel, print tab, dialogs):
//
//   spacing  4 / 8 / 12 / 16 / 24 px at 96 DPI, scaled by S() to the display
//   type     Segoe UI Variable Text (Windows 11) or Segoe UI, four sizes: caption 8.25, body 9,
//            heading 10 semibold, title 12 semibold (points: GDI scales them with the DPI itself)
//   colour   neutral blue accent; high-contrast text and borders on white; status colours for pills
//   shape    6 px corners, 1 px borders
//
// Use installed Windows system UI fonts (Segoe UI or Arial); no bundled typefaces.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace PhiMech.SolidWorks.UI.Controls
{
    public enum Tone
    {
        Neutral,
        Success,
        Warning,
        Danger,
        Busy,
    }

    public static class Ui
    {
        // ---- spacing (96-DPI px; always pass through S) ------------------------------------------
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space5 = 24;
        public const int Radius = 6;
        public const int ControlHeight = 32;

        // ---- colour ------------------------------------------------------------------------------
        /// <summary>Headings and primary text (neutral palette, 17:1 on white).</summary>
        public static readonly Color Ink = Color.FromArgb(0x16, 0x19, 0x22);
        /// <summary>Body text (neutral-700, 15:1).</summary>
        public static readonly Color Text = Color.FromArgb(0x20, 0x23, 0x2D);
        /// <summary>Secondary text (neutral-500, 7:1; legible on white).</summary>
        public static readonly Color Muted = Color.FromArgb(0x52, 0x58, 0x66);
        /// <summary>Placeholder text (4.7:1).</summary>
        public static readonly Color Placeholder = Color.FromArgb(0x6E, 0x74, 0x83);
        /// <summary>Disabled text (exempt from contrast rules, still readable).</summary>
        public static readonly Color Disabled = Color.FromArgb(0x86, 0x8C, 0x98);
        public static readonly Color Border = Color.FromArgb(0xE2, 0xE4, 0xE9);
        public static readonly Color BorderStrong = Color.FromArgb(0xCD, 0xD0, 0xD5);
        public static readonly Color BorderHover = Color.FromArgb(0x86, 0x8C, 0x98);
        /// <summary>Page background behind the cards (neutral-100).</summary>
        public static readonly Color Page = Color.FromArgb(0xF6, 0xF8, 0xFA);
        public static readonly Color Surface = Color.White;
        public static readonly Color Subtle = Color.FromArgb(0xEE, 0xF0, 0xF3);
        /// <summary>Links and accent text: dark neutral blue on white.</summary>
        public static readonly Color AccentText = Color.FromArgb(0x25, 0x50, 0x72);

        public static Color ToneFore(Tone t)
        {
            switch (t)
            {
                case Tone.Success: return Color.FromArgb(0x14, 0x6C, 0x2E);
                case Tone.Warning: return Color.FromArgb(0x8A, 0x45, 0x00);
                case Tone.Danger: return Color.FromArgb(0xB3, 0x26, 0x1E);
                case Tone.Busy: return Muted;
                default: return Color.FromArgb(0x3E, 0x43, 0x50);
            }
        }

        public static Color ToneBack(Tone t)
        {
            switch (t)
            {
                case Tone.Success: return Color.FromArgb(0xE6, 0xF4, 0xEA);
                case Tone.Warning: return Color.FromArgb(0xFF, 0xF1, 0xDC);
                case Tone.Danger: return Color.FromArgb(0xFD, 0xEC, 0xEA);
                default: return Subtle;
            }
        }

        /// <summary>A strong mark in the tone (severity dots, the gateway dot).</summary>
        public static Color ToneMark(Tone t)
        {
            switch (t)
            {
                case Tone.Success: return Color.FromArgb(0x1E, 0x9E, 0x4A);
                case Tone.Warning: return Color.FromArgb(0xF0, 0x8C, 0x00);
                case Tone.Danger: return Color.FromArgb(0xE5, 0x25, 0x1B);
                default: return Color.FromArgb(0xB8, 0xBC, 0xC4);
            }
        }

        // ---- scale -------------------------------------------------------------------------------
        private static float? _scale;

        /// <summary>
        /// Display scale (1.0 at 96 DPI, 1.5 at 150%). PHIMECH_UI_SCALE overrides it for the
        /// off-screen preview tool only, which then also scales the fonts to simulate the display.
        /// </summary>
        public static float Scale
        {
            get
            {
                if (_scale == null)
                {
                    var over = ScaleOverride;
                    if (over > 0) _scale = over;
                    else
                    {
                        try
                        {
                            using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                        }
                        catch (Exception)
                        {
                            _scale = 1f;
                        }
                    }
                }
                return _scale.Value;
            }
        }

        private static float ScaleOverride =>
            float.TryParse(System.Environment.GetEnvironmentVariable("PHIMECH_UI_SCALE") ?? "", NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) && v >= 0.5f && v <= 4f ? v : 0f;

        /// <summary>96-DPI pixels to device pixels.</summary>
        public static int S(int px) => ScaleBy(px, Scale);

        /// <summary>
        /// For sizes AutoScaleMode.Dpi scales itself (a dialog's width): unchanged on a real display;
        /// scaled only when the preview tool simulates a scale the display does not have.
        /// </summary>
        public static int PreviewOnlyScale(int px) => ScaleOverride > 0 ? S(px) : px;

        /// <summary>Pure form of S (tested).</summary>
        public static int ScaleBy(int px, float scale) => (int)Math.Round(px * scale, MidpointRounding.AwayFromZero);

        // ---- type --------------------------------------------------------------------------------
        private static readonly Lazy<HashSet<string>> Installed = new Lazy<HashSet<string>>(() =>
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                    return new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        });

        /// <summary>The first family of the list that is installed; the last one otherwise.</summary>
        public static string PickFamily(IEnumerable<string> preferred, ICollection<string> installed)
        {
            string last = null;
            foreach (var f in preferred)
            {
                last = f;
                if (installed.Contains(f)) return f;
            }
            return last;
        }

        public static readonly string Family = PickFamily(new[] { "Segoe UI Variable Text", "Segoe UI", "Arial" }, Installed.Value);

        /// <summary>The semibold face; null when only synthetic bold is available.</summary>
        private static readonly string SemiboldFamily =
            Family == "Segoe UI Variable Text" && Installed.Value.Contains("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : Installed.Value.Contains("Segoe UI Semibold") ? "Segoe UI Semibold" : null;

        private static float FontScale => ScaleOverride > 0 ? ScaleOverride : 1f;

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        private static Font Get(float pt, bool semibold, bool underline = false)
        {
            var key = pt + (semibold ? "s" : "r") + (underline ? "u" : "");
            lock (FontCache)
            {
                if (FontCache.TryGetValue(key, out var f)) return f;
                var style = underline ? FontStyle.Underline : FontStyle.Regular;
                if (semibold && SemiboldFamily == null) style |= FontStyle.Bold;
                f = new Font(semibold && SemiboldFamily != null ? SemiboldFamily : Family, pt * FontScale, style);
                FontCache[key] = f;
                return f;
            }
        }

        /// <summary>8.25 pt: hints, meta lines, footer.</summary>
        public static Font Caption => Get(8.25f, false);
        /// <summary>9 pt: body text, inputs.</summary>
        public static Font Body => Get(9f, false);
        /// <summary>9 pt semibold: buttons, labels, emphasis in body text.</summary>
        public static Font BodyStrong => Get(9f, true);
        /// <summary>10 pt semibold: card headings, tabs.</summary>
        public static Font Heading => Get(10f, true);
        /// <summary>12 pt semibold: the product name, dialog titles, big numbers.</summary>
        public static Font Title => Get(12f, true);
        public static Font Underlined(Font f) => Get(f.SizeInPoints / FontScale, f.Name != Family || f.Bold, true);

        // ---- drawing -----------------------------------------------------------------------------
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(0.5f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int key);

        /// <summary>
        /// True when focus is arriving from the keyboard (Tab / Shift+Tab / arrows held down right
        /// now): only then do controls draw a focus ring, so a click or the panel opening leaves none.
        /// </summary>
        public static bool FocusFromKeyboard()
        {
            try
            {
                foreach (var k in new[] { 0x09, 0x25, 0x26, 0x27, 0x28 })   // Tab, Left, Up, Right, Down
                    if (GetKeyState(k) < 0) return true;
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>"8 s", "1 min 05 s": elapsed time for progress lines.</summary>
        public static string Elapsed(TimeSpan t)
        {
            var s = (int)Math.Max(0, Math.Floor(t.TotalSeconds));
            return s < 60 ? s + " s" : (s / 60) + " min " + (s % 60).ToString("00", CultureInfo.InvariantCulture) + " s";
        }
    }
}
