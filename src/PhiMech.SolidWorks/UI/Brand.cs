// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
// Neutral accent colours for the add-in's panels and dialogs; no company artwork is bundled.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI
{
    internal static class Brand
    {
        private static readonly Color AccentStart = Color.FromArgb(0x25, 0x50, 0x72);
        private static readonly Color AccentMid = Color.FromArgb(0x32, 0x65, 0x84);
        private static readonly Color AccentEnd = Color.FromArgb(0x40, 0x77, 0x94);

        /// <summary>A neutral accent gradient across a rectangle.</summary>
        public static LinearGradientBrush Gradient(Rectangle r)
        {
            var brush = new LinearGradientBrush(r.Width > 0 && r.Height > 0 ? r : new Rectangle(0, 0, 1, 1),
                AccentStart, AccentEnd, 45f);
            brush.InterpolationColors = new ColorBlend
            {
                Colors = new[] { AccentStart, AccentMid, AccentEnd },
                Positions = new[] { 0f, 0.5f, 1f },
            };
            return brush;
        }

        /// <summary>A thin accent rule (height in 96-DPI pixels).</summary>
        public static Control GradientBar(int height)
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = Controls.Ui.S(height), TabStop = false };
            bar.Paint += (s, e) =>
            {
                using (var b = Gradient(bar.ClientRectangle)) e.Graphics.FillRectangle(b, bar.ClientRectangle);
            };
            bar.Resize += (s, e) => bar.Invalidate();
            return bar;
        }
    }
}
