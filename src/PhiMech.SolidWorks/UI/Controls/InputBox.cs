// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// A text box in the design system: 1 px rounded border (accent while focused), inner padding, and
// a real placeholder - drawn over the empty box, gone the moment you type, never part of the text
// (so there is nothing to delete first and nothing wrong gets sent). Works multi-line too, where
// the Win32 cue banner does not.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public sealed class InputBox : Panel, IAutoHeight
    {
        private readonly int _lines;

        /// <param name="lines">visible lines; above 1 the box is multi-line and scrolls</param>
        public InputBox(string placeholder = "", int lines = 1, string accessibleName = null)
        {
            _lines = Math.Max(1, lines);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Box = new CueTextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Ui.Body,
                ForeColor = Ui.Text,
                BackColor = Color.White,
                Multiline = _lines > 1,
                AcceptsReturn = _lines > 1,
                ScrollBars = ScrollBars.None,   // shown only once the text is longer than the box
                WordWrap = true,
                Placeholder = placeholder ?? "",
                AccessibleName = accessibleName ?? placeholder,
            };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.EnabledChanged += (s, e) => { Box.BackColor = Box.Enabled ? Color.White : Ui.Page; BackColor = Box.BackColor; Invalidate(); };
            Height = HeightFor(0);
        }

        public TextBox Box { get; }

        public override string Text
        {
            get => Box.Text;
            set => Box.Text = value;
        }

        public string Placeholder
        {
            get => ((CueTextBox)Box).Placeholder;
            set => ((CueTextBox)Box).Placeholder = value;
        }

        private int LineHeight => TextRenderer.MeasureText("Ag", Box.Font).Height;

        public int HeightFor(int width) => _lines == 1
            ? Math.Max(Ui.S(Ui.ControlHeight), LineHeight + Ui.S(Ui.Space2) * 2)
            : LineHeight * _lines + Ui.S(Ui.Space2) * 2;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var padX = Ui.S(Ui.Space2);
            if (_lines == 1)
            {
                var h = Box.PreferredHeight;
                Box.Bounds = new Rectangle(padX, (Height - h) / 2, Math.Max(1, Width - padX * 2), h);
            }
            else
            {
                var padY = Ui.S(Ui.Space1) + 2;
                Box.Bounds = new Rectangle(padX, padY, Math.Max(1, Width - padX - Ui.S(2)), Math.Max(1, Height - padY * 2));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var focused = Box.Focused;
            var w = focused ? 1.6f : 1f;
            var r = new RectangleF(w / 2, w / 2, Width - w, Height - w);
            using (var path = Ui.Rounded(r, Ui.S(Ui.Radius)))
            {
                using (var fill = new SolidBrush(Box.BackColor)) g.FillPath(fill, path);
                using (var pen = new Pen(!Box.Enabled ? Ui.Border : focused ? Ui.AccentText : Ui.BorderStrong, w)) g.DrawPath(pen, path);
            }
        }

        private sealed class CueTextBox : TextBox
        {
            private const int WmPaint = 0x000F;
            private const int EmGetRect = 0x00B2;
            private string _placeholder = "";

            [StructLayout(LayoutKind.Sequential)]
            private struct Rect
            {
                public int Left, Top, Right, Bottom;
            }

            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Rect lParam);

            public string Placeholder
            {
                get => _placeholder;
                set
                {
                    _placeholder = value ?? "";
                    Invalidate();
                }
            }

            [DllImport("user32.dll", EntryPoint = "SendMessage")]
            private static extern IntPtr SendMessageInt(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            protected override void OnTextChanged(EventArgs e)
            {
                base.OnTextChanged(e);
                if (TextLength <= 1) Invalidate();
                // Not inside the edit control's own change notification: toggling the scroll bar
                // recreates its window.
                if (IsHandleCreated && Multiline) BeginInvoke((Action)FitScrollBar);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                FitScrollBar();
            }

            /// <summary>A vertical scroll bar only when the text is taller than the box.</summary>
            private void FitScrollBar()
            {
                if (!Multiline || !IsHandleCreated) return;
                var lines = SendMessageInt(Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero).ToInt32();   // EM_GETLINECOUNT
                var visible = Math.Max(1, ClientSize.Height / Math.Max(1, TextRenderer.MeasureText("Ag", Font).Height));
                var need = TextLength > 0 && lines > visible;
                if (need == (ScrollBars == ScrollBars.Vertical)) return;
                var focused = Focused;
                var start = SelectionStart;
                var length = SelectionLength;
                ScrollBars = need ? ScrollBars.Vertical : ScrollBars.None;   // recreates the window
                Select(start, length);
                if (focused) Focus();
                ScrollToCaret();
            }

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                Invalidate();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != WmPaint || TextLength > 0 || _placeholder.Length == 0 || !IsHandleCreated) return;
                var rect = new Rect();
                SendMessage(Handle, EmGetRect, IntPtr.Zero, ref rect);
                var area = rect.Right > rect.Left
                    ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, Math.Max(rect.Bottom, ClientSize.Height))
                    : ClientRectangle;
                using (var g = CreateGraphics())
                {
                    TextRenderer.DrawText(g, _placeholder, Font, area, Ui.Placeholder, BackColor,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.TextBoxControl
                        | (Multiline ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis)
                        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
