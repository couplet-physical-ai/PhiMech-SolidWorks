// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The frame every PhiMech dialog shares (and the installer's dialogs copy): the gradient bar, a
// title, a one-line intro, the dialog's own content, and the
// buttons bottom-right (secondary, then the one primary). Laid out by the same Stack as the panel,
// so it fits its content at any DPI: the width is set in 96-DPI units and AutoScaleMode.Dpi scales
// it; the height follows the content on load.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace PhiMech.SolidWorks.UI.Controls
{
    public class BrandDialog : Form
    {
        private readonly Stack _content;
        private readonly FlowRow _buttons;
        private readonly TextBlock _intro;
        private readonly Control _bar;

        public BrandDialog(string windowTitle, string heading, string intro, int width = 440)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = windowTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = Ui.Body;
            ForeColor = Ui.Text;
            // The preview tool simulates a scaled display without one: scale the width itself there.
            ClientSize = new Size(Ui.PreviewOnlyScale(width), 200);

            _bar = Brand.GradientBar(3);
            _content = new Stack(Ui.Space2) { Inset = new Padding(Ui.Space5, Ui.Space5 - 4, Ui.Space5, Ui.Space5 - 4), AutoHeight = true, BackColor = Color.White };
            _content.Add(new DialogHeader(heading));
            _intro = _content.Add(new TextBlock(intro ?? "", Ui.Body, Ui.Muted), Ui.Space1);
            if (string.IsNullOrEmpty(intro)) Reveal.Set(_intro, false);
            Body = new Stack(Ui.Space2) { BackColor = Color.White };
            _content.Add(Body, Ui.Space4);
            _buttons = _content.Add(new FlowRow(Ui.Space2), Ui.Space5);
            _buttons.Add(new Spacer(), fill: true);
            Controls.Add(_content);
            Controls.Add(_bar);
            ResumeLayout(false);
        }

        /// <summary>The dialog's own content, between the intro and the buttons.</summary>
        protected Stack Body { get; }

        protected TextBlock Intro => _intro;

        /// <summary>Add a button bottom-right (add the primary last).</summary>
        protected BrandButton AddButton(string text, ButtonKind kind)
        {
            var b = new BrandButton(text, kind) { Width = Ui.S(96) };
            b.Width = Math.Max(Ui.S(96), b.PreferredWidth);
            return _buttons.Add(b);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_content == null) return;
            var top = _bar.Height;
            _content.Bounds = new Rectangle(0, top, ClientSize.Width, _content.HeightFor(ClientSize.Width));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitHeight();
        }

        /// <summary>Make the window exactly as tall as its content (after the DPI scaling).</summary>
        protected void FitHeight()
        {
            var h = _bar.Height + _content.HeightFor(ClientSize.Width);
            if (ClientSize.Height != h) ClientSize = new Size(ClientSize.Width, h);
            PerformLayout();
        }

        /// <summary>The dialog heading.</summary>
        private sealed class DialogHeader : Control, IAutoHeight
        {
            private readonly TextBlock _title;

            public DialogHeader(string heading)
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                BackColor = Color.White;
                _title = new TextBlock(heading ?? "", Ui.Title, Ui.Ink);
                Controls.Add(_title);
            }

            public int HeightFor(int width) => _title.HeightFor(width);

            protected override void OnLayout(LayoutEventArgs levent)
            {
                base.OnLayout(levent);
                _title.Bounds = new Rectangle(0, 0, Math.Max(1, Width), _title.HeightFor(Width));
            }
        }
    }
}
