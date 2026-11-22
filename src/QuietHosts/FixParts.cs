using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// One step of the Fix dialog (spec 5.2): a card (padding 16) with a numbered badge and the title (Body Strong, wraps),
    /// vertically centered on each other, 8 epx, the body (Body, secondary, wraps), and at the bottom left the step's button,
    /// if any, so the buttons of cards side by side line up. A group named "Step 1: Restart your browser" for screen
    /// readers; the button's description is the card's body. The badge is painted by the card (decorative: the number is
    /// in the group's name).
    /// </summary>
    internal sealed class StepCard : CardPanel
    {
        private readonly TypeRamp _ramp;
        private ThemedButton? _button;
        private int _number;
        private Rectangle _badge;

        public StepCard(TypeRamp ramp, int number, string title, string body) : base(Metrics.CardPadding, withContent: false)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            AutoSize = false;
            TitleLabel = new ThemedLabel(ramp, title, TextStyle.BodyStrong);
            BodyLabel = new ThemedLabel(ramp, body, TextStyle.Body, TextRole.Secondary);
            Controls.Add(TitleLabel);
            Controls.Add(BodyLabel);
            _number = number;
            UpdateName();
            ApplyTheme(ThemeManager.Current);
        }

        public ThemedLabel TitleLabel { get; }
        public ThemedLabel BodyLabel { get; }

        /// <summary>The step's number, in the badge and the group's name.</summary>
        public int Number
        {
            get => _number;
            set
            {
                if (_number == value) return;
                _number = value;
                UpdateName();
                Invalidate();
            }
        }

        public string Title
        {
            get => TitleLabel.Text;
            set
            {
                TitleLabel.Text = value ?? string.Empty;
                UpdateName();
            }
        }

        public string BodyText
        {
            get => BodyLabel.Text;
            set
            {
                BodyLabel.Text = value ?? string.Empty;
                if (_button != null) _button.ContextDescription = BodyLabel.Text;
            }
        }

        /// <summary>The step's button (shown or hidden by its owner), or null; its description is the card's body.</summary>
        public ThemedButton? Button
        {
            get => _button;
            set
            {
                if (ReferenceEquals(_button, value)) return;
                if (_button != null) Controls.Remove(_button);
                _button = value;
                if (_button != null)
                {
                    _button.ContextDescription = BodyLabel.Text;
                    Controls.Add(_button);
                }
                PerformLayout();
            }
        }

        private bool ButtonShown => _button != null && LayoutUtil.IsShown(_button);

        private void UpdateName() => AccessibleName = Strings.FixStepName(_number, TitleLabel.Text);

        /// <summary>The badge's diameter: max(Px(24), the Body Strong line).</summary>
        private int BadgeSize => Math.Max(_ramp.Scale.Px(Metrics.StepBadge), _ramp.LineHeight(TextStyle.BodyStrong));

        /// <summary>
        /// The narrowest the card may be beside other cards: 200 epx, or its button's preferred width plus 32 epx (its padding
        /// and some room). Narrower cards stack (D18).
        /// </summary>
        public int MinSideBySideWidth
        {
            get
            {
                UiScale s = _ramp.Scale;
                int button = ButtonShown ? _button!.GetPreferredSize(Size.Empty).Width + s.Px(32) : 0;
                return Math.Max(s.Px(200), button);
            }
        }

        /// <summary>The card's height at <paramref name="width"/> pixels: its content and, below a 12 epx gap, its button.</summary>
        public int HeightFor(int width) => Arrange(width, 0, apply: false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            // The base constructor's padding lays out before this card's parts exist.
            if (BodyLabel == null || ClientSize.Width <= 0) return;
            SuspendLayout();
            try
            {
                Arrange(ClientSize.Width, ClientSize.Height, apply: true);
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        private int Arrange(int width, int height, bool apply)
        {
            UiScale s = _ramp.Scale;
            int pad = s.Px(Metrics.CardPadding);
            int inner = Math.Max(1, width - 2 * pad);
            int badge = BadgeSize;
            int gap = s.Px(Metrics.ButtonGap);
            int titleWidth = Math.Max(1, inner - badge - gap);
            TitleLabel.MaximumSize = new Size(titleWidth, 0);
            Size title = TitleLabel.GetPreferredSize(new Size(titleWidth, 0));
            int row = Math.Max(badge, title.Height);
            int y = pad + row + gap;
            BodyLabel.MaximumSize = new Size(inner, 0);
            Size body = BodyLabel.GetPreferredSize(new Size(inner, 0));
            int bodyY = y;
            y += body.Height;
            Size button = Size.Empty;
            if (ButtonShown)
            {
                button = _button!.GetPreferredSize(Size.Empty);
                y += s.Px(12) + button.Height;
            }
            int natural = y + pad;
            if (apply)
            {
                _badge = new Rectangle(pad, pad + (row - badge) / 2, badge, badge);
                TitleLabel.SetBounds(pad + badge + gap, pad + (row - title.Height) / 2, Math.Min(titleWidth, title.Width), title.Height);
                BodyLabel.SetBounds(pad, bodyY, Math.Min(inner, body.Width), body.Height);
                if (ButtonShown)
                {
                    // At the bottom: in a row of cards as tall as the tallest, every button sits on the same line.
                    int bottom = Math.Max(natural, height) - pad;
                    _button!.SetBounds(pad, bottom - button.Height, button.Width, button.Height);
                }
                Invalidate();
            }
            return natural;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_badge.Width <= 0) return;
            Theme t = ThemeManager.Current;
            Graphics g = e.Graphics;
            // A circle with a 1 epx outline and the number in Body Strong, both AccentText (WindowText in high contrast).
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            RoundRect.Stroke(g, _badge, _badge.Width / 2f, _ramp.Scale.Stroke(Metrics.Border), t.AccentText);
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
            TextRenderer.DrawText(g, Strings.StepNumber(_number), _ramp.BodyStrong, _badge, t.AccentText,
                TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }

        /// <summary>Tests: the badge's bounds in the card.</summary>
        internal Rectangle BadgeBounds => _badge;
    }

    /// <summary>
    /// The Fix dialog's row of step cards (spec 5.2): side by side, equally wide, 8 epx apart, as tall as the tallest card
    /// (every card fills it); when a card would be narrower than its button needs or than 200 epx, they stack instead, each
    /// full width and as tall as its own content, 8 epx apart (D18).
    /// </summary>
    internal sealed class StepRow : Panel, IThemed
    {
        private readonly TypeRamp _ramp;
        private readonly List<StepCard> _cards = new List<StepCard>();

        public StepRow(TypeRamp ramp, params StepCard[] cards)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            // A layout panel has no name (Windows would otherwise name it after the intro above it).
            AccessibleName = string.Empty;
            foreach (StepCard c in cards)
            {
                _cards.Add(c);
                Controls.Add(c);
            }
        }

        /// <summary>The last layout stacked the cards.</summary>
        public bool IsStacked { get; private set; }

        public void ApplyTheme(Theme theme)
        {
            BackColor = Color.Transparent;
            Invalidate();
        }

        /// <summary>The row's height at <paramref name="width"/> pixels.</summary>
        public int HeightFor(int width) => Arrange(width, apply: false, out _);

        /// <summary>The cards stack at <paramref name="width"/> pixels.</summary>
        public bool StacksAt(int width)
        {
            Arrange(width, apply: false, out bool stacked);
            return stacked;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (ClientSize.Width <= 0) return;
            Arrange(ClientSize.Width, apply: true, out bool stacked);
            IsStacked = stacked;
        }

        private int Arrange(int width, bool apply, out bool stacked)
        {
            var shown = new List<StepCard>();
            foreach (StepCard c in _cards)
            {
                if (LayoutUtil.IsShown(c)) shown.Add(c);
            }
            stacked = false;
            if (shown.Count == 0) return 0;
            int gap = _ramp.Scale.Px(Metrics.ButtonGap);
            int cardWidth = (width - gap * (shown.Count - 1)) / shown.Count;
            foreach (StepCard c in shown)
            {
                if (cardWidth < c.MinSideBySideWidth) stacked = true;
            }
            if (!stacked)
            {
                int height = 0;
                foreach (StepCard c in shown) height = Math.Max(height, c.HeightFor(cardWidth));
                if (apply)
                {
                    int x = 0;
                    for (int i = 0; i < shown.Count; i++)
                    {
                        // The last card takes the pixels the division left over, so the row ends on the column's edge.
                        int w = i == shown.Count - 1 ? width - x : cardWidth;
                        shown[i].SetBounds(x, 0, w, height);
                        x += w + gap;
                    }
                }
                return height;
            }
            int y = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                int h = shown[i].HeightFor(width);
                if (apply) shown[i].SetBounds(0, y, width, h);
                y += h + (i < shown.Count - 1 ? gap : 0);
            }
            return y;
        }
    }

    /// <summary>
    /// Two labels that read as one line (the Fix dialog's count and its " · 2 selected", spec 5.9): both are tight (no GDI
    /// margins), so the second follows the first's last letter with only the space its text starts with. When both don't
    /// fit on one line, the second goes on the next. Each stays a label of its own for screen readers: only the first is a
    /// live region. It wraps to the width it gets, like a label.
    /// </summary>
    internal sealed class LabelPair : Panel, IThemed, IWrapsText
    {
        public LabelPair(ThemedLabel first, ThemedLabel second)
        {
            First = first ?? throw new ArgumentNullException(nameof(first));
            Second = second ?? throw new ArgumentNullException(nameof(second));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AccessibleName = string.Empty;
            First.Tight = true;
            Second.Tight = true;
            foreach (ThemedLabel label in new[] { First, Second })
            {
                // The pair gives each label its room when it measures it and its size when it places it (Arrange): a label
                // that sizes itself takes its unwrapped size whatever bounds it is given, so at 150% text the pair cut a
                // count that wraps off after its first line ("No blocked address matches" lost the search text).
                label.MaximumSize = Size.Empty;
                label.AutoSize = false;
                // A new text or font lays the pair out again (a label that doesn't size itself doesn't ask for it).
                label.TextChanged += OnPartChanged;
                label.FontChanged += OnPartChanged;
            }
            Controls.Add(First);
            Controls.Add(Second);
        }

        public ThemedLabel First { get; }
        public ThemedLabel Second { get; }

        public void ApplyTheme(Theme theme)
        {
            BackColor = Color.Transparent;
            Invalidate();
        }

        private void OnPartChanged(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            PerformLayout();
            Parent?.PerformLayout(this, nameof(Bounds));
        }

        private int WidthLimit(Size proposed)
        {
            int w = proposed.Width > 1 && proposed.Width < 100000 ? proposed.Width : int.MaxValue;
            if (MaximumSize.Width > 0) w = Math.Min(w, MaximumSize.Width);
            return w;
        }

        public override Size GetPreferredSize(Size proposedSize) => Arrange(WidthLimit(proposedSize), apply: false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (ClientSize.Width <= 0) return;
            Arrange(ClientSize.Width, apply: true);
        }

        private Size Arrange(int width, bool apply)
        {
            Size first = First.GetPreferredSize(new Size(width, 0));
            if (apply) First.SetBounds(0, 0, Math.Min(width, first.Width), first.Height);
            if (!LayoutUtil.IsShown(Second)) return first;
            Size second = Second.GetPreferredSize(Size.Empty);
            bool oneLine = first.Height <= First.MinimumSize.Height + 1 || first.Height < 2 * First.Font.Height;
            if (oneLine && first.Width + second.Width <= width)
            {
                if (apply) Second.SetBounds(first.Width, 0, second.Width, second.Height);
                return new Size(first.Width + second.Width, Math.Max(first.Height, second.Height));
            }
            // On a line of its own its text starts at the first one's edge: the spaces it starts with (" · 2 selected") sit
            // left of the pair, where nothing shows them.
            int lead = LeadingSpaceWidth(Second);
            second = Second.GetPreferredSize(new Size(width + lead, 0));
            if (apply) Second.SetBounds(-lead, first.Height, Math.Min(width + lead, second.Width), second.Height);
            return new Size(Math.Max(first.Width, second.Width - lead), first.Height + second.Height);
        }

        /// <summary>How wide the spaces a tight label's text starts with are (0 for a label with GDI's margins).</summary>
        private static int LeadingSpaceWidth(ThemedLabel label)
        {
            string text = label.Text ?? string.Empty;
            string trimmed = text.TrimStart(' ');
            if (!label.Tight || trimmed.Length == text.Length || trimmed.Length == 0) return 0;
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            return Math.Max(0, GdiText.Measure(text, label.Font, flags).Width - GdiText.Measure(trimmed, label.Font, flags).Width);
        }
    }

    /// <summary>
    /// What the Fix dialog's list shows when a search matches nothing (spec 5.8): over the list's rows (the list stays, with
    /// no items, for screen readers), centered, the no-match line, 4 epx, the hint (secondary), 8 epx and "Go to step N",
    /// which moves the focus to the last step card's button. The owner hides the link when that card has no enabled button.
    /// </summary>
    internal sealed class EmptyStatePanel : Panel, IThemed
    {
        private readonly TypeRamp _ramp;

        public EmptyStatePanel(TypeRamp ramp)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AccessibleName = string.Empty;
            Message = new ThemedLabel(ramp, string.Empty, TextStyle.Body, TextRole.Primary) { TextAlign = ContentAlignment.TopCenter };
            Hint = new ThemedLabel(ramp, Strings.FixNoMatchHint, TextStyle.Body, TextRole.Secondary) { TextAlign = ContentAlignment.TopCenter };
            GoToStep = new ThemedLink(ramp, Strings.FixGoToStep(2));
            Controls.Add(Message);
            Controls.Add(Hint);
            Controls.Add(GoToStep);
            ApplyTheme(ThemeManager.Current);
        }

        public ThemedLabel Message { get; }
        public ThemedLabel Hint { get; }
        /// <summary>"Go to step N": the owner sets its text and whether it shows.</summary>
        public ThemedLink GoToStep { get; }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.CardBackground;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(ThemeManager.Current.CardBackground)) e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (GoToStep == null || ClientSize.Width <= 0) return;
            UiScale s = _ramp.Scale;
            int width = Math.Max(1, ClientSize.Width - 2 * s.Px(Metrics.CardPadding));
            Message.MaximumSize = new Size(width, 0);
            Hint.MaximumSize = new Size(width, 0);
            Size message = Message.GetPreferredSize(new Size(width, 0));
            Size hint = Hint.GetPreferredSize(new Size(width, 0));
            bool link = LayoutUtil.IsShown(GoToStep);
            Size go = link ? GoToStep.GetPreferredSize(Size.Empty) : Size.Empty;
            int total = message.Height + s.Px(4) + hint.Height + (link ? s.Px(Metrics.ButtonGap) + go.Height : 0);
            int y = Math.Max(0, (ClientSize.Height - total) / 2);
            Message.SetBounds((ClientSize.Width - message.Width) / 2, y, message.Width, message.Height);
            y += message.Height + s.Px(4);
            Hint.SetBounds((ClientSize.Width - hint.Width) / 2, y, hint.Width, hint.Height);
            y += hint.Height;
            if (link) GoToStep.SetBounds((ClientSize.Width - go.Width) / 2, y + s.Px(Metrics.ButtonGap), go.Width, go.Height);
        }
    }
}
