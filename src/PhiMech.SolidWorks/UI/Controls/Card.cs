// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Card: a white section with a 1 px border and rounded corners, a heading (and optionally a status
// pill on the right and a one-line subtitle), then its content stacked below. Pill: a rounded
// status badge (PASS green, needs review amber, FAIL red, checking... with a spinner).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public class Card : Stack
    {
        private readonly HeaderRow _header;
        private readonly TextBlock _subtitle;

        public Card(string title, string subtitle = null) : base(Ui.Space2)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
            Inset = new Padding(Ui.Space3, Ui.Space3, Ui.Space3, Ui.Space3);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = title;
            _header = new HeaderRow(title);
            Add(_header);
            _subtitle = Add(new TextBlock(subtitle ?? "", Ui.Caption, Ui.Muted), gapBefore: Ui.Space1);
            if (string.IsNullOrEmpty(subtitle)) Reveal.Set(_subtitle, false);
        }

        public string Title
        {
            get => _header.Title.Text;
            set => _header.Title.Text = value;
        }

        public string Subtitle
        {
            get => _subtitle.Text;
            set
            {
                _subtitle.Text = value ?? "";
                Reveal.Set(_subtitle, !string.IsNullOrEmpty(value));
            }
        }

        /// <summary>The pill at the right of the heading (hidden while its text is empty).</summary>
        public Pill Pill => _header.Pill;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Ui.Rounded(r, Ui.S(Ui.Radius)))
            {
                using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
                using (var pen = new Pen(Ui.Border)) g.DrawPath(pen, path);
            }
        }

        /// <summary>Title on the left (wrapping), the pill on the right, top-aligned.</summary>
        private sealed class HeaderRow : Control, IAutoHeight
        {
            public readonly TextBlock Title;
            public readonly Pill Pill;

            public HeaderRow(string title)
            {
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                TabStop = false;
                Title = new TextBlock(title, Ui.Heading, Ui.Ink) { AccessibleRole = AccessibleRole.StaticText };
                Pill = new Pill();
                Controls.Add(Title);
                Controls.Add(Pill);
                Pill.TextChanged += (s, e) =>
                {
                    PerformLayout();
                    Parent?.PerformLayout();
                };
            }

            private int PillWidth => string.IsNullOrEmpty(Pill.Text) ? 0 : Pill.PreferredWidth;

            public int HeightFor(int width)
            {
                var pw = PillWidth;
                var tw = Math.Max(Ui.S(60), width - (pw > 0 ? pw + Ui.S(Ui.Space2) : 0));
                return Math.Max(Title.HeightFor(tw), pw > 0 ? Pill.Height : 0);
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                base.OnLayout(levent);
                var pw = PillWidth;
                var tw = Math.Max(Ui.S(60), Width - (pw > 0 ? pw + Ui.S(Ui.Space2) : 0));
                var th = Title.HeightFor(tw);
                Title.Bounds = new Rectangle(0, Math.Max(0, (Pill.Height - th) / 2), tw, th);
                Pill.Visible = pw > 0;
                if (pw > 0) Pill.Bounds = new Rectangle(Width - pw, 0, pw, Pill.Height);
            }
        }
    }

    public sealed class Pill : Control
    {
        private Tone _tone = Tone.Neutral;
        private readonly Timer _timer = new Timer { Interval = 60 };
        private float _spin;

        public Pill(string text = "", Tone tone = Tone.Neutral)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Font = Ui.BodyStrong;
            AccessibleRole = AccessibleRole.StaticText;
            Height = TextRenderer.MeasureText("Ag", Font).Height + Ui.S(4);
            _timer.Tick += (s, e) =>
            {
                _spin = (_spin + 30) % 360;
                Invalidate();
            };
            Set(text, tone);
        }

        public Tone Tone => _tone;

        public void Set(string text, Tone tone)
        {
            _tone = tone;
            Text = text ?? "";
            AccessibleName = "Status: " + Text;
            if (tone == Tone.Busy && Text.Length > 0) _timer.Start();
            else _timer.Stop();
            Invalidate();
            Parent?.PerformLayout();
        }

        private bool Spinner => _tone == Tone.Busy;

        public int PreferredWidth =>
            string.IsNullOrEmpty(Text) ? 0
            : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width
              + Ui.S(Ui.Space3) * 2 + (Spinner ? Ui.S(14) : 0);

        protected override void OnPaint(PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(Text)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Ui.Rounded(r, r.Height / 2))
            using (var fill = new SolidBrush(Ui.ToneBack(_tone)))
                g.FillPath(fill, path);
            var fore = Ui.ToneFore(_tone);
            var x = Ui.S(Ui.Space3);
            if (Spinner)
            {
                var d = Ui.S(9);
                var arc = new Rectangle(x - Ui.S(2), (Height - d) / 2, d, d);
                using (var track = new Pen(Color.FromArgb(50, fore), Ui.S(2) > 2 ? 2f : 1.6f)) g.DrawEllipse(track, arc);
                using (var pen = new Pen(fore, Ui.S(2) > 2 ? 2f : 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, arc, _spin, 110);
                x += Ui.S(12);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
