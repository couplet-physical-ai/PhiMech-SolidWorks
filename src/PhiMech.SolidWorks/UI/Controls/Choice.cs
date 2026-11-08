// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Picking one of a few options, by mouse or keyboard:
//
//   TabStrip   the panel tabs: semibold ink on the selected tab over a
//              gradient underline, muted text on the others, a hairline under the strip
//   Segmented  a compact Low | Medium | High switch (the AI review depth)
//
// Both take focus (Tab), move with the arrow keys / Home / End, and tell accessibility tools what
// is selected.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public abstract class ChoiceBase : Control, IAutoHeight
    {
        protected readonly string[] Names;
        private bool _keyboardFocus;
        private int _hover = -1;

        protected ChoiceBase(string[] names)
        {
            Names = names;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex { get; private set; }

        public string SelectedName => Names[SelectedIndex];

        public event Action<int> Selected;

        public void Select(int index)
        {
            if (index < 0 || index >= Names.Length) return;
            SelectedIndex = index;
            AccessibleDescription = Names[index] + " selected (" + (index + 1) + " of " + Names.Length + ")";
            Invalidate();
            Selected?.Invoke(index);
        }

        public abstract int HeightFor(int width);

        protected abstract Rectangle ItemRect(int i);

        protected bool KeyboardFocus => _keyboardFocus && Focused;

        protected int Hover => _hover;

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var i = SelectedIndex;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Up: i = Math.Max(0, i - 1); break;
                case Keys.Right:
                case Keys.Down: i = Math.Min(Names.Length - 1, i + 1); break;
                case Keys.Home: i = 0; break;
                case Keys.End: i = Names.Length - 1; break;
                default: return;
            }
            _keyboardFocus = true;
            e.Handled = true;
            if (i != SelectedIndex) Select(i);
            else Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _keyboardFocus = false;
            if (e.Button != MouseButtons.Left) return;
            for (var i = 0; i < Names.Length; i++)
                if (ItemRect(i).Contains(e.Location))
                {
                    if (CanFocus) Focus();
                    Select(i);
                }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = -1;
            for (var i = 0; i < Names.Length; i++)
                if (ItemRect(i).Contains(e.Location)) h = i;
            if (h != _hover)
            {
                _hover = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _keyboardFocus = Ui.FocusFromKeyboard();
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _keyboardFocus = false;
            Invalidate();
        }

        protected Color ParentBack()
        {
            for (var p = Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Color.White;
        }
    }

    public sealed class TabStrip : ChoiceBase
    {
        public TabStrip(params string[] names) : base(names)
        {
            BackColor = Color.White;
            Font = Ui.Heading;
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = "PhiMech sections";
            Height = HeightFor(0);
            Select(0);
        }

        public override int HeightFor(int width) => TextRenderer.MeasureText("Ag", Font).Height + Ui.S(Ui.Space3) + Ui.S(3);

        protected override Rectangle ItemRect(int i)
        {
            var x = 0;
            for (var k = 0; k < i; k++) x += ItemWidth(k);
            return new Rectangle(x, 0, ItemWidth(i), Height);
        }

        private int ItemWidth(int i) =>
            TextRenderer.MeasureText(Names[i], Ui.Heading, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width
            + Ui.S(Ui.Space4) * 2;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var line = new SolidBrush(Ui.Border)) g.FillRectangle(line, 0, Height - 1, Width, 1);
            for (var i = 0; i < Names.Length; i++)
            {
                var r = ItemRect(i);
                var selected = i == SelectedIndex;
                var fore = selected ? Ui.Ink : i == Hover ? Ui.Text : Ui.Muted;
                TextRenderer.DrawText(g, Names[i], Ui.Heading, new Rectangle(r.X, r.Y, r.Width, r.Height - Ui.S(3)),
                    fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (selected)
                {
                    var bar = new Rectangle(r.X + Ui.S(Ui.Space2), Height - Ui.S(3), r.Width - Ui.S(Ui.Space2) * 2, Ui.S(3));
                    using (var b = Brand.Gradient(bar)) g.FillRectangle(b, bar);
                }
                if (selected && KeyboardFocus)
                {
                    var ring = new Rectangle(r.X + 2, 2, r.Width - 5, r.Height - Ui.S(3) - 5);
                    using (var pen = new Pen(Ui.AccentText) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, ring);
                }
            }
        }
    }

    public sealed class Segmented : ChoiceBase
    {
        public Segmented(string accessibleName, params string[] names) : base(names)
        {
            Font = Ui.Body;
            AccessibleRole = AccessibleRole.ComboBox;
            AccessibleName = accessibleName;
            Height = HeightFor(0);
        }

        public override int HeightFor(int width) => Ui.S(Ui.ControlHeight);

        protected override Rectangle ItemRect(int i)
        {
            var inner = Width - Ui.S(4);
            var w = inner / Names.Length;
            var x = Ui.S(2) + i * w;
            return new Rectangle(x, Ui.S(2), i == Names.Length - 1 ? inner - w * i : w, Height - Ui.S(4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentBack());
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Ui.Rounded(outer, Ui.S(Ui.Radius)))
            {
                using (var fill = new SolidBrush(Ui.Subtle)) g.FillPath(fill, path);
                using (var pen = new Pen(Ui.Border)) g.DrawPath(pen, path);
            }
            for (var i = 0; i < Names.Length; i++)
            {
                var r = ItemRect(i);
                var selected = i == SelectedIndex;
                if (selected)
                {
                    using (var path = Ui.Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), Math.Max(1, Ui.S(Ui.Radius) - 2)))
                    {
                        using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
                        using (var pen = new Pen(KeyboardFocus ? Ui.AccentText : Ui.BorderStrong, KeyboardFocus ? 1.6f : 1f)) g.DrawPath(pen, path);
                    }
                }
                var fore = !Enabled ? Ui.Disabled : selected ? Ui.Ink : i == Hover ? Ui.Text : Ui.Muted;
                TextRenderer.DrawText(g, Names[i], selected ? Ui.BodyStrong : Ui.Body, r, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
