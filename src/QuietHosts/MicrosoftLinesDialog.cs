using System;
using System.Drawing;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// "Lines that block Microsoft addresses" (spec 6.6), from the notice's "Show lines": the other programs' hosts lines
    /// that raise the notice, read-only, with "How to remove them" (the README's steps) and Copy lines. These are the
    /// user's own hosts lines: they are never added to Copy details or LastRun, and QuietHosts never changes them.
    /// </summary>
    internal sealed class MicrosoftLinesDialog : ThemedDialog
    {
        private const int ContentWidth = Metrics.MicrosoftLinesDialogWidth - 2 * Metrics.PagePadding;
        private readonly UiContext _ctx;
        private readonly string _lines;

        public MicrosoftLinesDialog(UiContext ctx, HostsStatus s) : base(Strings.MicrosoftLinesTitle, Metrics.MicrosoftLinesDialogWidth)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            if (s == null) throw new ArgumentNullException(nameof(s));
            UiScale sc = Scale;
            _lines = string.Join("\r\n", s.MicrosoftLines);

            // The body's left padding leaves the link's 4 epx padding before its text, so its text lines up with the rest.
            int ringRoom = sc.Px(4);
            int pad = sc.Px(Metrics.PagePadding);
            Body.Padding = new Padding(pad - ringRoom, pad, pad, pad);
            var heading = new ThemedLabel(Ramp, Strings.MicrosoftLinesTitle, TextStyle.Subtitle, TextRole.Primary, ContentWidth) { Margin = new Padding(ringRoom, 0, 0, 0) };
            var intro = new ThemedLabel(Ramp, Strings.MicrosoftLinesIntro, TextStyle.Body, TextRole.Primary, ContentWidth) { Margin = new Padding(ringRoom, sc.Px(12), 0, 0) };
            Lines = new CodeBox(Ramp, TextStyle.Mono, multiline: true, ContentWidth, 200, Strings.MicrosoftLinesTitle)
            {
                Margin = new Padding(ringRoom, sc.Px(Metrics.HeadingGap), 0, 0),
            };
            // Scroll bars only where the lines don't fit (none for a few short lines).
            Lines.SetText(_lines);
            int more = s.MicrosoftLineCount - s.MicrosoftLines.Count;
            More = new ThemedLabel(Ramp, more > 0 ? Strings.MicrosoftLinesMore(more) : string.Empty, TextStyle.Body, TextRole.Secondary, ContentWidth)
            {
                Margin = new Padding(ringRoom, sc.Px(4), 0, 0),
                Visible = more > 0,
            };
            Feedback = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary, ContentWidth)
            {
                Margin = new Padding(ringRoom, sc.Px(4), 0, 0),
                Visible = false,
            };
            RemoveLink = new ThemedLink(Ramp, Strings.HowToRemoveLink) { External = true, Margin = new Padding(0, sc.Px(Metrics.HeadingGap), 0, 0) };
            RemoveLink.Invoked += (o, e) => OpenUrl(Strings.DefenderHelpUrl);
            Ui.AddRow(Body, heading);
            Ui.AddRow(Body, intro);
            Ui.AddRow(Body, Lines);
            Ui.AddRow(Body, More);
            Ui.AddRow(Body, Feedback);
            Ui.AddRow(Body, RemoveLink);

            CopyButton = new ThemedButton(Ramp, Strings.CopyLinesButton);
            CopyButton.Click += (o, e) => CopyLines();
            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(CopyButton, CloseControl);
            AcceptButton = CloseControl;
            CancelButton = CloseControl;
            // The heading has no period of its own.
            Finish(CloseControl, Strings.MicrosoftLinesTitle + ". " + Strings.MicrosoftLinesIntro);
        }

        /// <summary>The lines, read-only (Mono, no word wrap, a scroll bar where they don't fit).</summary>
        public CodeBox Lines { get; }
        /// <summary>"And N more lines." when the status holds fewer lines than the file has.</summary>
        public ThemedLabel More { get; }
        /// <summary>"Lines copied." after Copy lines (or that it failed).</summary>
        public ThemedLabel Feedback { get; }
        public ThemedLink RemoveLink { get; }
        public ThemedButton CopyButton { get; }
        public ThemedButton CloseControl { get; }

        /// <summary>Exactly the lines the box shows go on the clipboard; the line under the box says whether it worked.</summary>
        private void CopyLines()
        {
            bool copied;
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewClipboard != null) copied = PreviewClipboard(_lines);
            else
#endif
                // A preview copies nothing and shows what a copy that worked would.
                copied = _ctx.Preview || DetailsReport.TryCopy(_lines);
            Feedback.Text = copied ? Strings.LinesCopied : Strings.LinesNotCopied;
            Feedback.Visible = true;
            Ui.Announce(this, Feedback.Text, Announcement.Result);
            if (IsHandleCreated) FitToWorkingArea(OwnerWorkingArea());
        }

        private void OpenUrl(string url)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewOpenUrl != null)
            {
                PreviewOpenUrl(url);
                return;
            }
#endif
            Links.OpenUrl(_ctx, this, url);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For tests: the clipboard (returns whether the copy worked).</summary>
        internal Func<string, bool>? PreviewClipboard { get; set; }

        /// <summary>For tests: links open here instead of in the browser (a preview opens nothing anyway).</summary>
        internal Action<string>? PreviewOpenUrl { get; set; }
#endif
    }

    /// <summary>
    /// A read-only text box in a bordered card (spec 6.5, 6.6, 6.8): the code font or Body, selectable, themed (its fill and
    /// text from the tokens, its scroll bars dark in dark mode, again after a handle is recreated). Always named, because a
    /// text box's text is its content, not a name.
    /// </summary>
    internal sealed class CodeBox : CardPanel
    {
        private const int InnerPadding = 8;
        private readonly int _wrapLines;

        /// <param name="widthEpx">The card's width; the text box fills it inside the padding (8 epx unless given).</param>
        /// <param name="heightEpx">Multi-line: the card's height. One line: ignored (the card is as tall as its lines plus the padding).</param>
        /// <param name="paddingEpx">The card's padding around the text box (the License card has 12).</param>
        /// <param name="wordWrap">Multi-line text that wraps to the box, with a vertical scroll bar only (the License).</param>
        /// <param name="wrapLines">
        /// Not multi-line: the text (an address or a folder) wraps instead of running past the box's edge, and the card is as
        /// tall as its lines, at most this many (then a vertical scroll bar); 0 keeps one line.
        /// </param>
        public CodeBox(TypeRamp ramp, TextStyle style, bool multiline, int widthEpx, int heightEpx, string accessibleName,
            int paddingEpx = InnerPadding, bool wordWrap = false, int wrapLines = 0) : base(paddingEpx, withContent: false)
        {
            if (ramp == null) throw new ArgumentNullException(nameof(ramp));
            UiScale s = ramp.Scale;
            AutoSize = false;
            _wrapLines = multiline ? 0 : Math.Max(0, wrapLines);
            bool wraps = _wrapLines > 0;
            TextBox = new CodeTextBox(this)
            {
                Multiline = multiline || wraps,
                ReadOnly = true,
                WordWrap = (multiline && wordWrap) || wraps,
                ScrollBars = !multiline ? ScrollBars.None : wordWrap ? ScrollBars.Vertical : ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                Font = ramp.Font(style),
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Margin = Padding.Empty,
            };
            Controls.Add(TextBox);
            int height = multiline ? s.Px(heightEpx) : HeightForLines(1);
            SetFixedSize(new Size(s.Px(widthEpx), height));
            ApplyTheme(ThemeManager.Current);
        }

        public TextBox TextBox { get; }

        /// <summary>How many lines the card shows now (a wrapping box).</summary>
        internal int ShownLines { get; private set; } = 1;

        /// <summary>
        /// The text. A wrapping box takes the height of its lines (at most its limit, then a vertical scroll bar); a multi-line
        /// box without word wrap shows a scroll bar only for the direction its text overflows (6.6: none for three short lines).
        /// </summary>
        public void SetText(string? text)
        {
            TextBox.Text = text ?? string.Empty;
            if (_wrapLines > 0) FitLines(EstimateLines());
            else if (TextBox.Multiline && !TextBox.WordWrap) FitScrollBars();
        }

        private int HeightForLines(int lines) => TextBox.PreferredHeight + (Math.Max(1, lines) - 1) * TextBox.Font.Height + Padding.Vertical;

        private void SetFixedSize(Size size)
        {
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Size = size;
            MinimumSize = size;
            MaximumSize = size;
        }

        /// <summary>The wrapped lines as GDI wraps an edit control's text, before the box exists (a little narrower, never fewer lines).</summary>
        private int EstimateLines()
        {
            if (TextBox.TextLength == 0) return 1;
            int width = Math.Max(1, Width - Padding.Horizontal - UiScale.Current.Px(6));
            Size text = GdiText.Measure(TextBox.Text, TextBox.Font, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix,
                new Size(width, int.MaxValue));
            return Math.Max(1, (int)Math.Ceiling(text.Height / (double)Math.Max(1, TextBox.Font.Height)));
        }

        /// <summary>A wrapping box shows <paramref name="lines"/> lines, at most its limit (then with a vertical scroll bar).</summary>
        /// <param name="fromTextBox">
        /// Called from the text box's own messages (its handle's creation, a size change): a new scroll bar recreates its
        /// handle, which must wait for the next message. Recreated inside its WM_CREATE, the handle's creation failed ("Error
        /// creating window handle": the open-failure dialog at 225% text, whose address took one line more than estimated).
        /// </param>
        internal void FitLines(int lines, bool fromTextBox = false)
        {
            if (_wrapLines <= 0) return;
            int shown = Math.Max(1, Math.Min(lines, _wrapLines));
            ScrollBars bars = lines > _wrapLines ? ScrollBars.Vertical : ScrollBars.None;
            if (TextBox.ScrollBars != bars)
            {
                if (fromTextBox) PostRecount();
                else TextBox.ScrollBars = bars;
            }
            ShownLines = shown;
            int height = HeightForLines(shown);
            if (height == Height) return;
            SetFixedSize(new Size(Width, height));
            Parent?.PerformLayout(this, nameof(Bounds));
        }

        private bool _recountPosted;

        /// <summary>Counts the text box's lines again after the current message (and then changes its scroll bar).</summary>
        private void PostRecount()
        {
            if (_recountPosted || !IsHandleCreated || IsDisposed) return;
            _recountPosted = true;
            BeginInvoke((Action)(() =>
            {
                _recountPosted = false;
                if (IsDisposed || TextBox.IsDisposed || !TextBox.IsHandleCreated) return;
                int lines = NativeTheme.EditLineCount(TextBox);
                if (lines > 0) FitLines(lines);
            }));
        }

        /// <summary>A multi-line box without word wrap: a scroll bar only where the text doesn't fit (both for many long lines).</summary>
        private void FitScrollBars()
        {
            var room = new Size(Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            string[] lines = TextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int height = lines.Length * TextBox.Font.Height;
            int widest = 0;
            foreach (string line in lines) widest = Math.Max(widest, GdiText.Measure(line, TextBox.Font, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width);
            // The edit control's own margins left and right.
            widest += UiScale.Current.Px(6);
            bool vertical = height > room.Height;
            bool horizontal = widest > room.Width;
            if (horizontal && !vertical) vertical = height > room.Height - SystemInformation.HorizontalScrollBarHeight;
            if (vertical && !horizontal) horizontal = widest > room.Width - SystemInformation.VerticalScrollBarWidth;
            ScrollBars bars = vertical && horizontal ? ScrollBars.Both : vertical ? ScrollBars.Vertical : horizontal ? ScrollBars.Horizontal : ScrollBars.None;
            if (TextBox.ScrollBars != bars) TextBox.ScrollBars = bars;
        }

        public override void ApplyTheme(Theme theme)
        {
            ((CodeTextBox)TextBox).ApplyTheme(theme);
            base.ApplyTheme(theme);
        }

        public override Size GetPreferredSize(Size proposedSize) => Size;

        /// <summary>The text box inside: card colors, and the native theme name for its scroll bars.</summary>
        private sealed class CodeTextBox : TextBox, IThemed
        {
            private readonly CodeBox _owner;

            public CodeTextBox(CodeBox owner) => _owner = owner;

            public void ApplyTheme(Theme theme)
            {
                BackColor = theme.CardBackground;
                ForeColor = theme.TextPrimary;
                if (IsHandleCreated) NativeTheme.SetTheme(Handle, theme.NativeThemeName);
                Invalidate();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // A recreated handle loses its theme name.
                NativeTheme.SetTheme(Handle, ThemeManager.Current.NativeThemeName);
                FitToLines();
            }

            protected override void OnClientSizeChanged(EventArgs e)
            {
                base.OnClientSizeChanged(e);
                FitToLines();
            }

            /// <summary>A wrapping box: the edit control's own count of its wrapped lines decides the card's height.</summary>
            private void FitToLines()
            {
                if (!IsHandleCreated || !Multiline || !WordWrap || ClientSize.Width <= 0) return;
                int lines = NativeTheme.EditLineCount(this);
                if (lines > 0) _owner.FitLines(lines, fromTextBox: true);
            }
        }
    }
}
