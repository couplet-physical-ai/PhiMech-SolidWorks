// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Layout for a Task Pane that can be 300 to 650 px wide: nothing is positioned by hand.
//
//   Stack     children top to bottom, full width (or left / right aligned), a gap between them;
//             its height follows its content, so wrapped text never clips
//   FlowRow   children left to right, wrapping onto the next line when the pane is narrow
//   TextBlock wrapping text that reports the height it needs for a width
//
// Anything that implements IAutoHeight tells its parent how tall it is at a given width; other
// controls (text boxes, combo boxes, buttons) keep their own height.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public interface IAutoHeight
    {
        int HeightFor(int width);
    }

    public enum Align
    {
        Stretch,
        Left,
        Right,
    }

    internal static class LayoutUtil
    {
        public static int HeightOf(Control c, int width)
        {
            if (c is IAutoHeight a) return a.HeightFor(width);
            return c.Height;
        }

        public static int PreferredWidth(Control c, int max)
        {
            if (c is BrandButton b) return Math.Min(max, b.PreferredWidth);
            if (c is Pill p) return Math.Min(max, p.PreferredWidth);
            return Math.Min(max, c.Width);
        }

        /// <summary>True unless the control was hidden itself (its parent being hidden does not count).</summary>
        public static bool Shown(Control c) => !(c.Tag is Hidden);

        public sealed class Hidden
        {
            public static readonly Hidden Mark = new Hidden();
        }
    }

    /// <summary>Show / hide a control inside a Stack or FlowRow (use this, not Visible).</summary>
    public static class Reveal
    {
        public static void Set(Control c, bool shown)
        {
            if (c == null) return;
            var was = LayoutUtil.Shown(c);
            c.Tag = shown ? null : (object)LayoutUtil.Hidden.Mark;
            c.Visible = shown;
            if (was != shown) c.Parent?.PerformLayout();
        }
    }

    public static class ScrollHelp
    {
        /// <summary>
        /// Scroll the nearest scrolling ancestor so <paramref name="c"/> shows (after the pending
        /// layout), e.g. a result card that appears below the fold.
        /// </summary>
        public static void IntoView(Control c)
        {
            if (c == null || !c.IsHandleCreated) return;
            try
            {
                c.BeginInvoke((Action)(() =>
                {
                    for (var p = c.Parent; p != null; p = p.Parent)
                        if (p is ScrollableControl s && s.AutoScroll)
                        {
                            s.ScrollControlIntoView(c);
                            return;
                        }
                }));
            }
            catch (InvalidOperationException)
            {
                // closing
            }
        }
    }

    /// <summary>Vertical stack. Gaps and padding are 96-DPI px; they are scaled here.</summary>
    public class Stack : Panel, IAutoHeight
    {
        private readonly Dictionary<Control, (int gap, Align align)> _items = new Dictionary<Control, (int, Align)>();
        private readonly List<Control> _order = new List<Control>();
        private bool _laying;
        private bool _again;

        public Stack(int gap = Ui.Space2)
        {
            Gap = gap;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Default gap between children, 96-DPI px.</summary>
        public int Gap { get; set; }

        /// <summary>Padding, 96-DPI px (scaled when laid out).</summary>
        public Padding Inset { get; set; }

        /// <summary>Set the height to the content's on every layout.</summary>
        public bool AutoHeight { get; set; } = true;

        public T Add<T>(T control, int? gapBefore = null, Align align = Align.Stretch) where T : Control
        {
            _items[control] = (gapBefore ?? -1, align);
            _order.Add(control);
            control.TabIndex = _order.Count;
            Controls.Add(control);
            return control;
        }

        public void RemoveItem(Control control)
        {
            if (!_items.Remove(control)) return;
            _order.Remove(control);
            Controls.Remove(control);
        }

        public IEnumerable<Control> Items => _order;

        public int HeightFor(int width) => Arrange(width, place: false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Width <= 0) return;
            if (_laying)
            {
                // Setting our height made the parent re-lay us out at another width (a scroll bar
                // came or went): do it again once the current pass returns, never skip it.
                _again = true;
                return;
            }
            _laying = true;
            try
            {
                var passes = 0;
                do
                {
                    _again = false;
                    var h = Arrange(ClientSize.Width, place: true);
                    if (AutoHeight && Height != h) Height = h;
                }
                while (_again && ++passes < 4);
            }
            finally
            {
                _laying = false;
            }
            Settle();
        }

        private bool _settlePosted;
        private int _settles;

        /// <summary>
        /// The outermost stack checks once the layout storm is over (a scroll bar appearing changes
        /// its width from inside its own layout): if its height no longer matches its content at
        /// its final width, lay out again. Bounded, so a scroll bar that flips each pass cannot loop.
        /// </summary>
        private void Settle()
        {
            if (!AutoHeight || Parent is Stack || _settlePosted || !IsHandleCreated) return;
            _settlePosted = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _settlePosted = false;
                    if (IsDisposed || Width <= 0) return;
                    if (HeightFor(ClientSize.Width) != Height && _settles++ < 3) PerformLayout();
                    else _settles = 0;
                }));
            }
            catch (InvalidOperationException)
            {
                _settlePosted = false;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) PerformLayout();
        }

        private int Arrange(int width, bool place)
        {
            var pad = Scaled(Inset);
            var inner = Math.Max(1, width - pad.Horizontal);
            var y = pad.Top;
            var first = true;
            foreach (var c in _order)
            {
                if (!LayoutUtil.Shown(c)) continue;
                var (gap, align) = _items[c];
                if (!first) y += Ui.S(gap >= 0 ? gap : Gap);
                first = false;
                var w = align == Align.Stretch ? inner : LayoutUtil.PreferredWidth(c, inner);
                var h = LayoutUtil.HeightOf(c, w);
                if (place)
                {
                    var x = pad.Left + (align == Align.Right ? inner - w : 0);
                    var r = new Rectangle(x, y, w, h);
                    if (c.Bounds != r) c.Bounds = r;
                    // Same bounds, but its content may have been laid out for another width while
                    // hidden (a tab page resized in the background): lay it out again.
                    else if (c is IAutoHeight && c.HasChildren) c.PerformLayout();
                }
                y += h;
            }
            return y + pad.Bottom;
        }

        internal static Padding Scaled(Padding p) => new Padding(Ui.S(p.Left), Ui.S(p.Top), Ui.S(p.Right), Ui.S(p.Bottom));
    }

    /// <summary>One item of a FlowRow for the pure arrangement.</summary>
    public struct FlowSpec
    {
        public FlowSpec(int width, bool fill = false, int minWidth = 0)
        {
            Width = width;
            Fill = fill;
            MinWidth = minWidth;
        }

        public int Width { get; }
        /// <summary>Takes the room left on its line (at least MinWidth, else it wraps).</summary>
        public bool Fill { get; }
        public int MinWidth { get; }
    }

    public struct FlowPlace
    {
        public int Line;
        public int X;
        public int Width;

        public override string ToString() => Line + ":" + X + "+" + Width;
    }

    public static class FlowLayout
    {
        /// <summary>
        /// Left to right, wrapping when the next item would not fit; fill items share what is left
        /// of their line. No item is ever wider than the available width (tested).
        /// </summary>
        public static IList<FlowPlace> Arrange(IList<FlowSpec> items, int gap, int available)
        {
            available = Math.Max(1, available);
            var places = new FlowPlace[items.Count];
            var line = 0;
            var x = 0;
            var lineStart = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var need = Math.Min(available, items[i].Fill ? Math.Max(1, items[i].MinWidth) : items[i].Width);
                if (x > 0 && x + need > available)
                {
                    Distribute(items, places, lineStart, i, gap, available);
                    line++;
                    x = 0;
                    lineStart = i;
                }
                places[i] = new FlowPlace { Line = line, X = x, Width = need };
                x += need + gap;
            }
            Distribute(items, places, lineStart, items.Count, gap, available);
            return places;
        }

        private static void Distribute(IList<FlowSpec> items, FlowPlace[] places, int from, int to, int gap, int available)
        {
            if (to <= from) return;
            var used = 0;
            var fills = 0;
            for (var i = from; i < to; i++)
            {
                used += places[i].Width + (i > from ? gap : 0);
                if (items[i].Fill) fills++;
            }
            var spare = Math.Max(0, available - used);
            var x = 0;
            for (var i = from; i < to; i++)
            {
                var w = places[i].Width;
                if (items[i].Fill && fills > 0)
                {
                    var extra = spare / fills;
                    w += extra;
                    spare -= extra;
                    fills--;
                }
                places[i] = new FlowPlace { Line = places[i].Line, X = x, Width = Math.Min(available, w) };
                x += places[i].Width + gap;
            }
        }
    }

    /// <summary>Left-to-right row that wraps; children keep their height, centred on their line.</summary>
    public class FlowRow : Panel, IAutoHeight
    {
        private readonly List<(Control c, bool fill, int min)> _items = new List<(Control, bool, int)>();
        private bool _laying;
        private bool _again;

        public FlowRow(int gap = Ui.Space2)
        {
            Gap = gap;
        }

        public int Gap { get; set; }

        /// <param name="fill">take the rest of the line (min width 96-DPI px, else wrap)</param>
        public T Add<T>(T control, bool fill = false, int minWidth = 0) where T : Control
        {
            _items.Add((control, fill, minWidth));
            control.TabIndex = _items.Count;
            Controls.Add(control);
            return control;
        }

        public int HeightFor(int width) => Arrange(width, place: false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Width <= 0) return;
            if (_laying)
            {
                _again = true;   // as in Stack: re-run, never skip
                return;
            }
            _laying = true;
            try
            {
                var passes = 0;
                do
                {
                    _again = false;
                    var h = Arrange(ClientSize.Width, place: true);
                    if (Height != h) Height = h;
                }
                while (_again && ++passes < 4);
            }
            finally
            {
                _laying = false;
            }
        }

        private int Arrange(int width, bool place)
        {
            var shown = _items.Where(i => LayoutUtil.Shown(i.c)).ToList();
            if (shown.Count == 0) return 0;
            var specs = shown.Select(i => new FlowSpec(LayoutUtil.PreferredWidth(i.c, int.MaxValue), i.fill, Ui.S(i.min))).ToList();
            var places = FlowLayout.Arrange(specs, Ui.S(Gap), width);
            var lines = places.Max(p => p.Line) + 1;
            var heights = new int[lines];
            for (var i = 0; i < shown.Count; i++)
                heights[places[i].Line] = Math.Max(heights[places[i].Line], LayoutUtil.HeightOf(shown[i].c, places[i].Width));
            var tops = new int[lines];
            for (var l = 1; l < lines; l++) tops[l] = tops[l - 1] + heights[l - 1] + Ui.S(Gap);
            if (place)
            {
                for (var i = 0; i < shown.Count; i++)
                {
                    var p = places[i];
                    var h = LayoutUtil.HeightOf(shown[i].c, p.Width);
                    var r = new Rectangle(p.X, tops[p.Line] + (heights[p.Line] - h) / 2, p.Width, h);
                    if (shown[i].c.Bounds != r) shown[i].c.Bounds = r;
                    else if (shown[i].c is IAutoHeight && shown[i].c.HasChildren) shown[i].c.PerformLayout();
                }
            }
            return tops[lines - 1] + heights[lines - 1];
        }
    }

    /// <summary>Wrapping, non-selectable text that knows how tall it is.</summary>
    public class TextBlock : Control, IAutoHeight
    {
        private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl
                                              | TextFormatFlags.NoPrefix;

        public TextBlock(string text = "", Font font = null, Color? color = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Font = font ?? Ui.Body;
            ForeColor = color ?? Ui.Text;
            Text = text ?? "";
            AccessibleRole = AccessibleRole.StaticText;
        }

        /// <summary>At most this many lines, the last one ending in "..." (0 = no limit).</summary>
        public int MaxLines { get; set; }

        public int HeightFor(int width)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            var h = Measure(new Size(Math.Max(1, width), int.MaxValue)).Height;
            if (MaxLines > 0) h = Math.Min(h, LineHeight * MaxLines);
            return h;
        }

        /// <summary>
        /// Measured on this control's own device context once it has one: inside SolidWorks the
        /// screen DC measured text a few pixels wider than the pane's DC draws it, so a line that
        /// fits was given room for two (for example, a two-line card subtitle).
        /// </summary>
        private Size Measure(Size proposed)
        {
            if (IsHandleCreated)
            {
                try
                {
                    using (var g = CreateGraphics()) return TextRenderer.MeasureText(g, Text, Font, proposed, Flags);
                }
                catch (Exception)
                {
                    // fall back to the screen DC
                }
            }
            return TextRenderer.MeasureText(Text, Font, proposed, Flags);
        }

        private int LineHeight => TextRenderer.MeasureText("Ag", Font, new Size(int.MaxValue, int.MaxValue), Flags).Height;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            Invalidate();
            Parent?.PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Parent?.PerformLayout();   // measure again on the real DC
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Parent?.PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = Flags | (MaxLines > 0 ? TextFormatFlags.EndEllipsis : 0);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Ui.Disabled, flags);
        }
    }

    /// <summary>A 1 px horizontal rule.</summary>
    public sealed class Rule : Control
    {
        public Rule()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Ui.Border;
            Height = 1;
        }
    }
}

namespace PhiMech.SolidWorks.UI.Controls
{
    /// <summary>A flexible gap (fill it in a FlowRow to push what follows to the right).</summary>
    public sealed class Spacer : Control
    {
        public Spacer()
        {
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            Height = 1;
            Width = 0;
        }
    }
}
