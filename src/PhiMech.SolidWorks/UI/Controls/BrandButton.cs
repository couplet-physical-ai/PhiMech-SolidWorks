// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The three kinds of action, drawn the same way everywhere:
//
//   Primary    a neutral accent gradient, white semibold text - at most one per section
//   Secondary  white with a 1 px border
//   Link       accent text only, underlined on hover / keyboard focus - tertiary actions
//
// All three are real Buttons: they take focus, fire on Enter / Space and on a mouse click, and
// report themselves to accessibility tools (Link as a link). A busy button shows a small spinner
// and its busy text; a disabled one is gray.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public enum ButtonKind
    {
        Primary,
        Secondary,
        Link,
    }

    public class BrandButton : Button, IAutoHeight
    {
        private bool _hover;
        private bool _pressed;
        private bool _keyboardFocus;
        private bool _busy;
        private string _idleText;
        private float _spin;
        private Timer _spinner;

        public BrandButton(string text, ButtonKind kind = ButtonKind.Secondary)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Text = text;
            Font = kind == ButtonKind.Link ? Ui.Body : Ui.BodyStrong;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            AccessibleRole = kind == ButtonKind.Link ? AccessibleRole.Link : AccessibleRole.PushButton;
            Height = kind == ButtonKind.Link ? LinkHeight : Ui.S(Ui.ControlHeight);
        }

        public ButtonKind Kind { get; }

        /// <summary>Text shown (with a spinner) while Busy; null keeps the normal text.</summary>
        public string BusyText { get; set; }

        /// <summary>Busy: spinner + BusyText, and not clickable.</summary>
        public bool Busy
        {
            get => _busy;
            set
            {
                if (_busy == value) return;
                _busy = value;
                if (value)
                {
                    _idleText = Text;
                    if (BusyText != null) Text = BusyText;
                    _spinner = _spinner ?? new Timer { Interval = 60 };
                    _spinner.Tick -= OnSpin;
                    _spinner.Tick += OnSpin;
                    _spinner.Start();
                }
                else
                {
                    _spinner?.Stop();
                    if (_idleText != null) Text = _idleText;
                }
                Invalidate();
            }
        }

        private void OnSpin(object sender, EventArgs e)
        {
            _spin = (_spin + 30) % 360;
            Invalidate();
        }

        private int LinkHeight => TextRenderer.MeasureText("Ag", Font).Height + Ui.S(4);

        public int PreferredWidth
        {
            get
            {
                var text = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                if (Kind == ButtonKind.Link) return text + Ui.S(4);
                return text + Ui.S(Ui.Space4) * 2 + (_busy ? Ui.S(18) : 0);
            }
        }

        public int HeightFor(int width) => Kind == ButtonKind.Link ? LinkHeight : Ui.S(Ui.ControlHeight);

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            Parent?.PerformLayout();
        }

        protected override void OnClick(EventArgs e)
        {
            if (_busy) return;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            // A ring only for keyboard focus: clicking does not leave a ring behind.
            _keyboardFocus = Ui.FocusFromKeyboard();
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _keyboardFocus = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = ParentBack();
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var radius = Ui.S(Ui.Radius);
            var enabled = Enabled && !_busy;
            Color fore;

            if (Kind == ButtonKind.Link)
            {
                fore = Enabled ? (_pressed ? Ui.Ink : Ui.AccentText) : Ui.Disabled;
                var font = (_hover || _keyboardFocus) && Enabled ? Ui.Underlined(Font) : Font;
                TextRenderer.DrawText(g, Text, font, ClientRectangle, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                if (_keyboardFocus && Focused)
                {
                    using (var pen = new Pen(Ui.AccentText) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
                return;
            }

            using (var path = Ui.Rounded(r, radius))
            {
                if (Kind == ButtonKind.Primary)
                {
                    if (Enabled)
                    {
                        using (var brush = Brand.Gradient(Rectangle.Round(r))) g.FillPath(brush, path);
                        var shade = _pressed ? 48 : _hover && enabled ? 24 : _busy ? 16 : 0;
                        if (shade > 0)
                            using (var dark = new SolidBrush(Color.FromArgb(shade, 0, 0, 0))) g.FillPath(dark, path);
                        fore = Color.White;
                    }
                    else
                    {
                        using (var fill = new SolidBrush(Ui.Subtle)) g.FillPath(fill, path);
                        fore = Ui.Disabled;
                    }
                }
                else
                {
                    var fillColor = !Enabled ? Ui.Page : _pressed ? Ui.Subtle : _hover && enabled ? Ui.Page : Color.White;
                    using (var fill = new SolidBrush(fillColor)) g.FillPath(fill, path);
                    using (var pen = new Pen(!Enabled ? Ui.Border : _hover && enabled ? Ui.BorderHover : Ui.BorderStrong))
                        g.DrawPath(pen, path);
                    fore = Enabled ? Ui.Ink : Ui.Disabled;
                }
            }

            if (_keyboardFocus && Focused)
            {
                var ring = new RectangleF(2.5f, 2.5f, Width - 5f, Height - 5f);
                using (var path = Ui.Rounded(ring, Math.Max(1, radius - 2)))
                using (var pen = new Pen(Kind == ButtonKind.Primary ? Color.White : Ui.AccentText, Ui.S(2) > 2 ? 2f : 1.5f))
                    g.DrawPath(pen, path);
            }

            var text = ClientRectangle;
            if (_busy)
            {
                // Spinner left of the text, the pair centred.
                var d = Ui.S(12);
                var tw = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                var total = d + Ui.S(6) + tw;
                var x0 = Math.Max(Ui.S(8), (Width - total) / 2);
                var arc = new Rectangle(x0, (Height - d) / 2, d, d);
                using (var track = new Pen(Color.FromArgb(60, fore), Ui.S(2)))
                    g.DrawEllipse(track, arc);
                using (var pen = new Pen(fore, Ui.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, arc, _spin, 100);
                text = new Rectangle(x0 + d + Ui.S(6), 0, Width - (x0 + d + Ui.S(6)), Height);
                TextRenderer.DrawText(g, Text, Font, text, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                return;
            }
            text.Inflate(-Ui.S(Ui.Space2), 0);
            TextRenderer.DrawText(g, Text, Font, text, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        private Color ParentBack()
        {
            for (var p = Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Color.White;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _spinner?.Dispose();
            base.Dispose(disposing);
        }
    }
}
