using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// A card (spec 2.8.6): a rounded 4 epx rectangle in CardBackground with a 1 epx CardBorder border, around one
    /// transparent <see cref="ThemedTable"/> (<see cref="Content"/>) for its content. A named card is a Grouping for screen
    /// readers (level cards, step cards); an unnamed one a Pane.
    /// </summary>
    internal class CardPanel : Panel, IThemed
    {
        private ThemeToken _fill = ThemeToken.CardBackground;
        private ThemeToken _border = ThemeToken.CardBorder;
        private readonly ThemedTable? _content;

        /// <param name="paddingEpx">16 for cards, 20 for the status card, 4 around the Fix dialog's list.</param>
        /// <param name="withContent">False for cards that lay out their own children (the notice bar).</param>
        public CardPanel(int paddingEpx = Metrics.CardPadding, bool withContent = true)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            // Unnamed until a card is named (level and step cards, notice bars): Windows would otherwise name it after a label
            // before it, and a screen reader would read that as the card's context.
            AccessibleName = string.Empty;
            int pad = UiScale.Current.Px(paddingEpx);
            Padding = new Padding(pad);
            if (withContent)
            {
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                _content = new ThemedTable { Dock = DockStyle.Fill };
                Controls.Add(_content);
            }
        }

        /// <summary>The card's content (a transparent one-column table; add columns as needed).</summary>
        public ThemedTable Content => _content ?? throw new InvalidOperationException("This card lays out its own children.");

        /// <summary>The fill (CardBackground by default; the notice bars use their severity's fill).</summary>
        public ThemeToken Fill
        {
            get => _fill;
            set
            {
                if (_fill == value) return;
                _fill = value;
                Invalidate();
            }
        }

        public ThemeToken BorderToken
        {
            get => _border;
            set
            {
                if (_border == value) return;
                _border = value;
                Invalidate();
            }
        }

        /// <summary>Gives the card the spec's minimum height (68 epx).</summary>
        public void UseMinimumHeight() => MinimumSize = new Size(MinimumSize.Width, UiScale.Current.Px(Metrics.CardMinHeight));

        /// <summary>Fixes the card's width (the main window's cards are 528 epx wide).</summary>
        public void SetFixedWidth(int widthPx)
        {
            MinimumSize = new Size(widthPx, MinimumSize.Height);
            MaximumSize = new Size(widthPx, 0);
        }

        public virtual void ApplyTheme(Theme theme) => Invalidate();

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The parent's surface in the corners, then the card.
            base.OnPaintBackground(e);
            Theme t = ThemeManager.Current;
            UiScale s = UiScale.Current;
            Graphics g = e.Graphics;
            RoundRect.Prepare(g);
            float radius = s.Px(Metrics.Radius);
            RoundRect.Fill(g, ClientRectangle, radius, t[_fill]);
            RoundRect.Stroke(g, ClientRectangle, radius, s.Stroke(t.CardBorderWidth), t[_border]);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new CardAccessibleObject(this);

        /// <summary>Grouping when the card has a name, Pane otherwise (an explicit AccessibleRole wins).</summary>
        private sealed class CardAccessibleObject : ControlAccessibleObject
        {
            private readonly CardPanel _card;

            public CardAccessibleObject(CardPanel card) : base(card) { _card = card; }

            public override AccessibleRole Role
            {
                get
                {
                    if (_card.AccessibleRole != AccessibleRole.Default) return _card.AccessibleRole;
                    return string.IsNullOrEmpty(_card.AccessibleName) ? AccessibleRole.Pane : AccessibleRole.Grouping;
                }
            }
        }
    }

    /// <summary>A notice bar's severity (spec 2.8.7).</summary>
    internal enum NoticeSeverity
    {
        Error,
        Warning,
        Info,
        Tip,
        Success,
    }

    /// <summary>
    /// One notice (spec 2.8.7, an InfoBar look-alike): a severity glyph, the message, at most one action below it and a
    /// feedback line below that; a close button when closable. A Grouping named "Warning: …" for screen readers. Changing
    /// the message never announces by itself: the owner announces.
    /// </summary>
    internal sealed class NoticeBar : CardPanel
    {
        private readonly TypeRamp _ramp;
        private readonly GlyphIcon _glyph;
        private readonly ThemedLabel _message;
        private readonly ThemedLabel _feedback;
        private readonly ThemedButton? _close;
        private Control? _action;
        private NoticeSeverity _severity;
        private int _fixedWidth;
        private bool _feedbackShown;

        public NoticeBar(TypeRamp ramp, NoticeSeverity severity, string message, bool closable) : base(0, withContent: false)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            _severity = severity;
            Closable = closable;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _glyph = new GlyphIcon(GlyphFor(severity), Metrics.NoticeGlyph, GlyphColorFor(severity), Strings.NoticeGlyphName(severity));
            _message = new ThemedLabel(ramp, message ?? string.Empty);
            _feedback = new ThemedLabel(ramp, string.Empty, TextStyle.Body, TextRole.Secondary) { Visible = false };
            Controls.Add(_glyph);
            Controls.Add(_message);
            Controls.Add(_feedback);
            if (closable)
            {
                _close = new ThemedButton(ramp, Glyph.Close, Strings.CloseNoticeName, Metrics.NoticeClose) { TabIndex = 1 };
                _close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
                Controls.Add(_close);
            }
            Fill = FillFor(severity);
            UpdateNames();
            ApplyTheme(ThemeManager.Current);
        }

        public NoticeSeverity Severity
        {
            get => _severity;
            set
            {
                if (_severity == value) return;
                _severity = value;
                _glyph.Glyph = GlyphFor(value);
                _glyph.ColorToken = GlyphColorFor(value);
                _glyph.AccessibleName = Strings.NoticeGlyphName(value);
                Fill = FillFor(value);
                UpdateNames();
            }
        }

        /// <summary>Body, TextPrimary, wraps.</summary>
        public string Message
        {
            get => _message.Text;
            set
            {
                _message.Text = value ?? string.Empty;
                UpdateNames();
                Relayout();
            }
        }

        /// <summary>One ThemedButton or ThemedLink, placed below the message (null for none).</summary>
        public Control? Action
        {
            get => _action;
            set
            {
                if (ReferenceEquals(_action, value)) return;
                if (_action != null) Controls.Remove(_action);
                _action = value;
                if (_action != null)
                {
                    _action.TabIndex = 0;
                    Controls.Add(_action);
                    // Screen readers read the children in this order: the action right after the message, the close button last.
                    Controls.SetChildIndex(_action, Controls.GetChildIndex(_message) + 1);
                }
                Relayout();
            }
        }

        /// <summary>A secondary line under the action ("Details copied. …"); null hides it.</summary>
        public string? Feedback
        {
            get => _feedbackShown ? _feedback.Text : null;
            set
            {
                _feedbackShown = !string.IsNullOrEmpty(value);
                _feedback.Text = value ?? string.Empty;
                _feedback.Visible = _feedbackShown;
                Relayout();
            }
        }

        public bool Closable { get; }

        /// <summary>The close button (null when not closable).</summary>
        public ThemedButton? CloseButton => _close;

        /// <summary>The severity glyph (role Graphic, named).</summary>
        internal GlyphIcon SeverityGlyph => _glyph;

        /// <summary>The message label.</summary>
        internal ThemedLabel MessageLabel => _message;

        /// <summary>Raised by the close button; the owner removes or hides the bar.</summary>
        public event EventHandler? CloseRequested;

        /// <summary>The bar's width in pixels (0: the width its parent gives it).</summary>
        public int FixedWidth
        {
            get => _fixedWidth;
            set
            {
                _fixedWidth = value;
                Relayout();
            }
        }

        public static Glyph GlyphFor(NoticeSeverity severity)
        {
            switch (severity)
            {
                case NoticeSeverity.Error: return Glyph.Error;
                case NoticeSeverity.Warning: return Glyph.Warning;
                case NoticeSeverity.Success: return Glyph.Success;
                default: return Glyph.Info;
            }
        }

        public static ThemeToken GlyphColorFor(NoticeSeverity severity)
        {
            switch (severity)
            {
                case NoticeSeverity.Error: return ThemeToken.CriticalGlyph;
                case NoticeSeverity.Warning: return ThemeToken.CautionGlyph;
                case NoticeSeverity.Success: return ThemeToken.SuccessGlyph;
                default: return ThemeToken.InfoGlyph;
            }
        }

        /// <summary>Only Warning, Error and Success bars are colored; Info and Tip use the card fill.</summary>
        public static ThemeToken FillFor(NoticeSeverity severity)
        {
            switch (severity)
            {
                case NoticeSeverity.Error: return ThemeToken.CriticalBar;
                case NoticeSeverity.Warning: return ThemeToken.CautionBar;
                case NoticeSeverity.Success: return ThemeToken.SuccessBar;
                default: return ThemeToken.CardBackground;
            }
        }

        /// <summary>The bar's accessible name: the severity prefix and the message ("Tip: " is not doubled).</summary>
        public static string NameFor(NoticeSeverity severity, string message)
        {
            string prefix = Strings.NoticePrefix(severity);
            if (severity == NoticeSeverity.Tip && message.StartsWith(Strings.PrefixTip, StringComparison.Ordinal)) return message;
            return prefix + message;
        }

        private void UpdateNames()
        {
            AccessibleName = NameFor(_severity, _message.Text);
            if (_close != null) _close.AccessibleDescription = AccessibleName;
        }

        private void Relayout()
        {
            if (AutoSize) Parent?.PerformLayout(this, nameof(Bounds));
            PerformLayout();
            Invalidate();
        }

        // ---------------------------------------------------------------- layout (epx: padding 12/12/16/8, glyph 20, gap 12)

        private int RightReserve(UiScale s) => Closable
            ? s.Px(Metrics.NoticePadRight) + s.Px(Metrics.NoticeClose) + s.Px(Metrics.ButtonGap)
            : s.Px(Metrics.NoticePadRightNotClosable);

        private int MessageX(UiScale s) => s.Px(Metrics.NoticePadLeft) + s.Px(Metrics.NoticeGlyph) + s.Px(Metrics.NoticeGlyphGap);

        /// <summary>The content column's height for a bar of <paramref name="width"/> pixels (and sets the wrap widths).</summary>
        private int ColumnHeight(int width, UiScale s)
        {
            int column = Math.Max(1, width - MessageX(s) - RightReserve(s));
            _message.MaximumSize = new Size(column, 0);
            _feedback.MaximumSize = new Size(column, 0);
            int h = _message.GetPreferredSize(new Size(column, 0)).Height;
            if (_action != null) h += s.Px(Metrics.ButtonGap) + _action.GetPreferredSize(Size.Empty).Height;
            if (_feedbackShown) h += s.Px(4) + _feedback.GetPreferredSize(new Size(column, 0)).Height;
            return Math.Max(h, s.Px(Metrics.NoticeGlyph));
        }

        private int HeightFor(int width, UiScale s) =>
            Math.Max(s.Px(Metrics.NoticeMinHeight), s.Px(Metrics.NoticePadTop) + ColumnHeight(width, s) + s.Px(Metrics.NoticePadBottom));

        private int WidthFor(Size proposed)
        {
            if (_fixedWidth > 0) return _fixedWidth;
            if (proposed.Width > 0 && proposed.Width < 100000) return proposed.Width;
            return Width > 0 ? Width : UiScale.Current.Px(Metrics.MainClientWidth - 2 * Metrics.PagePadding);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = WidthFor(proposedSize);
            return new Size(w, HeightFor(w, UiScale.Current));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            UiScale s = UiScale.Current;
            int width = ClientSize.Width;
            if (width <= 0) return;
            SuspendLayout();
            try
            {
                int column = ColumnHeight(width, s);
                int height = Math.Max(s.Px(Metrics.NoticeMinHeight), s.Px(Metrics.NoticePadTop) + column + s.Px(Metrics.NoticePadBottom));
                // Shorter content than the minimum height: the extra room is split evenly above and below.
                int top = Math.Max(s.Px(Metrics.NoticePadTop), (height - column) / 2);
                int glyph = s.Px(Metrics.NoticeGlyph);
                _glyph.SetBounds(s.Px(Metrics.NoticePadLeft), top, glyph, glyph);
                int x = MessageX(s);
                int columnWidth = Math.Max(1, width - x - RightReserve(s));
                Size message = _message.GetPreferredSize(new Size(columnWidth, 0));
                _message.SetBounds(x, top, Math.Min(columnWidth, message.Width), message.Height);
                int y = top + message.Height;
                if (_action != null)
                {
                    y += s.Px(Metrics.ButtonGap);
                    Size action = _action.GetPreferredSize(Size.Empty);
                    // A link's text lines up with the message (its box has a 4 epx padding before the text).
                    int ax = _action is LinkLabel ? x - _action.Padding.Left : x;
                    _action.SetBounds(ax, y, action.Width, action.Height);
                    y += action.Height;
                }
                if (_feedbackShown)
                {
                    y += s.Px(4);
                    Size feedback = _feedback.GetPreferredSize(new Size(columnWidth, 0));
                    _feedback.SetBounds(x, y, Math.Min(columnWidth, feedback.Width), feedback.Height);
                }
                if (_close != null)
                {
                    int box = s.Px(Metrics.NoticeClose);
                    _close.SetBounds(width - s.Px(Metrics.NoticePadRight) - box, s.Px(8), box, box);
                }
            }
            finally
            {
                ResumeLayout(false);
            }
        }
    }

    /// <summary>The three pill looks (spec 2.8.8).</summary>
    internal enum PillKind
    {
        /// <summary>"Recommended", "Allowed by you".</summary>
        Accent,
        /// <summary>"Blocked".</summary>
        Neutral,
        /// <summary>"Strict": an outline, no fill.</summary>
        Outline,
    }

    /// <summary>
    /// Pills and tags (spec 2.8.8): Caption text, fully rounded. They are drawn, not controls; the text they show stays in
    /// the control that owns them, so screen readers read it.
    /// </summary>
    internal static class Pill
    {
        public static int Height(TypeRamp ramp) =>
            Math.Max(ramp.Scale.Px(Metrics.PillHeight), ramp.LineHeight(TextStyle.Caption) + ramp.Scale.Px(4));

        /// <summary>
        /// The pill's size: its text as drawn (NoPadding, measured on a device context, so GDI's blank margins don't count)
        /// plus 8 epx on each side (2.8.8).
        /// </summary>
        public static Size Measure(TypeRamp ramp, string text)
        {
            int h = Height(ramp);
            int textWidth = GdiText.Measure(text, ramp.Caption, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            return new Size(Math.Max(h, textWidth + 2 * ramp.Scale.Px(Metrics.PillPadX)), h);
        }

        /// <param name="selectedRow">The pill sits on a selected (or hovered) list row.</param>
        public static void Draw(Graphics g, TypeRamp ramp, PillKind kind, string text, Rectangle bounds, bool selectedRow, Theme theme)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            UiScale s = ramp.Scale;
            Color textColor;
            Color? fill = null, border = null;
            if (theme.IsHighContrast)
            {
                // No fill in high contrast: a WindowText border (HighlightText on a selected row).
                textColor = selectedRow ? theme.ListTextSelected : theme.PillAccentText;
                border = textColor;
            }
            else
            {
                switch (kind)
                {
                    case PillKind.Accent:
                        fill = theme.PillAccentFill;
                        textColor = theme.PillAccentText;
                        break;
                    case PillKind.Neutral:
                        fill = selectedRow ? theme.PillNeutralFillSelected : theme.PillNeutralFill;
                        textColor = theme.PillNeutralText;
                        break;
                    default:
                        border = theme.PillOutlineBorder;
                        textColor = theme.PillOutlineText;
                        break;
                }
            }
            var smoothing = g.SmoothingMode;
            var offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            float radius = bounds.Height / 2f;
            if (fill.HasValue) RoundRect.Fill(g, bounds, radius, fill.Value);
            if (border.HasValue) RoundRect.Stroke(g, bounds, radius, s.Stroke(1), border.Value);
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
            TextRenderer.DrawText(g, text, ramp.Caption, bounds, textColor,
                TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }

    /// <summary>
    /// A row of commands (spec 2.8.16): a left group and a right group, on one row when they fit (left + 16 epx + right),
    /// otherwise the left group on its own row and the right group below it. Right-group buttons are 8 epx apart in the
    /// given order ("do it" first, Cancel or Close last), right-aligned, wrapping onto more right-aligned rows (also when
    /// there is no left group). The left group's text wraps to the width it gets. Tab order and the order screen readers
    /// read: the left group, then the right group in visual order. When its content gets taller or shorter (a text that
    /// wraps, a line that comes or goes), the row asks its parent to place it again, so a footer docked at the bottom of a
    /// window always shows its buttons whole.
    /// </summary>
    internal class CommandRow : Panel, IThemed
    {
        private readonly List<Control> _left = new List<Control>();
        private readonly List<Control> _right = new List<Control>();
        private int _fixedWidth, _leftWrapMinWidth;
        private Func<int, int>? _leftReserve;
        private bool _redocking;

        public CommandRow(TypeRamp ramp)
        {
            Ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // A layout panel has no name: unnamed, Windows would name it after a label before it ("Check for a newer version").
            AccessibleName = string.Empty;
        }

        /// <summary>
        /// 0 (the default): the left group goes on its own row whenever it doesn't fit beside the right group unwrapped
        /// (2.8.16). Otherwise its text wraps beside the right group while that leaves it at least this many pixels, and only
        /// a narrower room puts it on its own row, whatever its text: the layout then doesn't change with the text (the Fix
        /// dialog's footer, whose left text changes with every selection).
        /// </summary>
        public int LeftWrapMinWidth
        {
            get => _leftWrapMinWidth;
            set
            {
                _leftWrapMinWidth = Math.Max(0, value);
                Relayout();
            }
        }

        /// <summary>
        /// The left group's smallest height in pixels for the room it gets (null: its own height). The Fix footer reserves
        /// the height of its longest common text there, so a click that changes the text never changes the footer's height.
        /// </summary>
        public Func<int, int>? LeftReserve
        {
            get => _leftReserve;
            set
            {
                _leftReserve = value;
                Relayout();
            }
        }

        protected TypeRamp Ramp { get; }

        public IReadOnlyList<Control> LeftControls => _left;
        public IReadOnlyList<Control> RightControls => _right;

        /// <summary>The row's width in pixels (0: the width its parent gives it). A fixed-width dialog sets it.</summary>
        public int FixedWidth
        {
            get => _fixedWidth;
            set
            {
                _fixedWidth = value;
                Relayout();
            }
        }

        /// <summary>The left group (labels, a progress ring, a link or a button), replacing the old one.</summary>
        public void SetLeft(params Control[] controls) => Replace(_left, controls);

        /// <summary>The right group, in visual order: "do it" first, Cancel or Close last.</summary>
        public void SetRight(params Control[] controls) => Replace(_right, controls);

        private void Replace(List<Control> group, Control[] controls)
        {
            SuspendLayout();
            foreach (Control c in group) Controls.Remove(c);
            group.Clear();
            foreach (Control c in controls)
            {
                if (c == null) continue;
                group.Add(c);
                Controls.Add(c);
            }
            // The child order is the order screen readers read in: the left group, then the right group (whichever was set
            // first); the tab order is the same.
            int index = 0;
            foreach (Control c in _left)
            {
                Controls.SetChildIndex(c, index);
                c.TabIndex = index++;
            }
            foreach (Control c in _right)
            {
                Controls.SetChildIndex(c, index);
                c.TabIndex = index++;
            }
            ResumeLayout(false);
            Relayout();
        }

        private void Relayout()
        {
            if (AutoSize) Parent?.PerformLayout(this, nameof(Bounds));
            PerformLayout();
        }

        /// <summary>Lays the groups out again after one of their texts changed (and the row's height with them).</summary>
        public void RefreshLayout() => PerformLayout();

        public virtual void ApplyTheme(Theme theme) => Invalidate();

        /// <summary>Tests: whether the last layout put the left group on its own row.</summary>
        internal bool IsStacked { get; private set; }

        /// <summary>Tests: how many rows the right group used in the last layout.</summary>
        internal int RightRows { get; private set; }

        private static IEnumerable<Control> ShownIn(List<Control> group)
        {
            foreach (Control c in group)
            {
                if (LayoutUtil.IsShown(c)) yield return c;
            }
        }

        private int NaturalWidth()
        {
            UiScale s = Ramp.Scale;
            int gap = s.Px(Metrics.ButtonGap);
            int left = GroupWidth(ShownIn(_left), gap);
            int right = GroupWidth(ShownIn(_right), gap);
            int both = left > 0 && right > 0 ? s.Px(Metrics.TextToButton) : 0;
            return left + both + right + Padding.Horizontal;
        }

        private static int GroupWidth(IEnumerable<Control> controls, int gap)
        {
            int w = 0, n = 0;
            foreach (Control c in controls)
            {
                w += Preferred(c, int.MaxValue).Width;
                n++;
            }
            return n == 0 ? 0 : w + (n - 1) * gap;
        }

        /// <summary>A control's size; a label, or a flow of labels (two texts on one line), wraps to <paramref name="maxWidth"/>.</summary>
        private static Size Preferred(Control c, int maxWidth)
        {
            if (Wraps(c) && maxWidth < int.MaxValue)
            {
                c.MaximumSize = new Size(Math.Max(1, maxWidth), 0);
                return c.GetPreferredSize(new Size(Math.Max(1, maxWidth), 0));
            }
            if (Wraps(c))
            {
                c.MaximumSize = Size.Empty;
                return c.GetPreferredSize(Size.Empty);
            }
            return c.GetPreferredSize(Size.Empty);
        }

        private static bool Wraps(Control c) => c is Label || c is FlowLayoutPanel || c is IWrapsText;

        private int WidthFor(Size proposed)
        {
            if (_fixedWidth > 0) return _fixedWidth;
            if (proposed.Width > 0 && proposed.Width < 100000) return proposed.Width;
            return NaturalWidth();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = WidthFor(proposedSize);
            return new Size(w, Arrange(w - Padding.Horizontal, apply: false) + Padding.Vertical);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int width = ClientSize.Width - Padding.Horizontal;
            if (width <= 0) return;
            int content;
            SuspendLayout();
            try
            {
                content = Arrange(width, apply: true);
            }
            finally
            {
                ResumeLayout(false);
            }
            // Its content needs another height now (a text that wraps onto another line, a line that came or went): the
            // parent places it again, so a footer docked at the bottom of a window never cuts its buttons off (5.9).
            if (AutoSize && !_redocking && Parent != null && content + Padding.Vertical != Height)
            {
                _redocking = true;
                try
                {
                    Parent.PerformLayout(this, nameof(Bounds));
                }
                finally
                {
                    _redocking = false;
                }
            }
        }

        /// <summary>Lays the groups out in <paramref name="width"/> pixels (with <paramref name="apply"/>: sets the bounds); returns the height.</summary>
        private int Arrange(int width, bool apply)
        {
            UiScale s = Ramp.Scale;
            int gap = s.Px(Metrics.ButtonGap);
            int textGap = s.Px(Metrics.TextToButton);
            var left = new List<Control>(ShownIn(_left));
            var right = new List<Control>(ShownIn(_right));
            var rightSizes = new List<Size>();
            foreach (Control c in right) rightSizes.Add(Preferred(c, int.MaxValue));
            int rightWidth = 0;
            foreach (Size z in rightSizes) rightWidth += z.Width;
            if (rightSizes.Count > 1) rightWidth += (rightSizes.Count - 1) * gap;
            int leftNatural = GroupWidth(left, gap);
            // Beside the right group: the left group's natural width fits (2.8.16), or in the wrapping mode the room left
            // for its text is wide enough. The right group alone stays on one row only while it fits too.
            bool beside = _leftWrapMinWidth > 0 ? width - rightWidth - textGap >= _leftWrapMinWidth : leftNatural + textGap + rightWidth <= width;
            bool oneRow = right.Count == 0 || (rightWidth <= width && (left.Count == 0 || beside));
            IsStacked = !oneRow;

            if (oneRow)
            {
                int leftRoom = right.Count == 0 ? width : Math.Max(1, width - rightWidth - textGap);
                int leftMin = LeftMin(left, leftRoom);
                List<Rectangle> leftBoxes = LayOutLeft(left, leftRoom, gap);
                int leftHeight = BottomOf(leftBoxes);
                int rightHeight = 0;
                foreach (Size z in rightSizes) rightHeight = Math.Max(rightHeight, z.Height);
                int height = Math.Max(Math.Max(leftHeight, rightHeight), leftMin);
                RightRows = right.Count > 0 ? 1 : 0;
                if (apply)
                {
                    int dy = (height - leftHeight) / 2;
                    for (int i = 0; i < left.Count; i++) Place(left[i], leftBoxes[i], 0, dy);
                    int x = width - rightWidth;
                    for (int i = 0; i < right.Count; i++)
                    {
                        Place(right[i], new Rectangle(x, (height - rightSizes[i].Height) / 2, rightSizes[i].Width, rightSizes[i].Height), 0, 0);
                        x += rightSizes[i].Width + gap;
                    }
                }
                return height;
            }

            // The left group (if any) on its own row, then the right group's rows, each right-aligned.
            int y = 0;
            if (left.Count > 0)
            {
                List<Rectangle> stackedLeft = LayOutLeft(left, width, gap);
                if (apply)
                {
                    for (int i = 0; i < left.Count; i++) Place(left[i], stackedLeft[i], 0, 0);
                }
                y = Math.Max(BottomOf(stackedLeft), LeftMin(left, width)) + gap;
            }
            int rows = 0;
            int start = 0;
            while (start < right.Count)
            {
                int end = start, rowWidth = rightSizes[start].Width, rowHeight = rightSizes[start].Height;
                while (end + 1 < right.Count && rowWidth + gap + rightSizes[end + 1].Width <= width)
                {
                    end++;
                    rowWidth += gap + rightSizes[end].Width;
                    rowHeight = Math.Max(rowHeight, rightSizes[end].Height);
                }
                if (apply)
                {
                    int x = Math.Max(0, width - rowWidth);
                    for (int i = start; i <= end; i++)
                    {
                        Place(right[i], new Rectangle(x, y + (rowHeight - rightSizes[i].Height) / 2, rightSizes[i].Width, rightSizes[i].Height), 0, 0);
                        x += rightSizes[i].Width + gap;
                    }
                }
                y += rowHeight;
                rows++;
                start = end + 1;
                if (start < right.Count) y += gap;
            }
            RightRows = rows;
            return y;
        }

        private int LeftMin(List<Control> left, int room) => left.Count > 0 && _leftReserve != null ? Math.Max(0, _leftReserve(Math.Max(1, room))) : 0;

        /// <summary>The left group's boxes: left to right, 8 epx apart; labels wrap to the room left; short parts centered on the first line.</summary>
        private List<Rectangle> LayOutLeft(List<Control> left, int room, int gap)
        {
            var boxes = new List<Rectangle>();
            int line = Ramp.LineHeight(TextStyle.Body);
            int x = 0;
            foreach (Control c in left)
            {
                Size z = Preferred(c, Wraps(c) ? Math.Max(1, room - x) : int.MaxValue);
                int y = z.Height < line ? (line - z.Height) / 2 : 0;
                boxes.Add(new Rectangle(x, y, z.Width, z.Height));
                x += z.Width + gap;
            }
            return boxes;
        }

        private static int BottomOf(List<Rectangle> boxes)
        {
            int b = 0;
            foreach (Rectangle r in boxes) b = Math.Max(b, r.Bottom);
            return b;
        }

        private void Place(Control c, Rectangle box, int dx, int dy) =>
            c.SetBounds(Padding.Left + box.X + dx, Padding.Top + box.Y + dy, box.Width, box.Height);
    }

    /// <summary>A control whose text wraps to the width it gets: <see cref="CommandRow"/> gives it its room as it gives a label.</summary>
    internal interface IWrapsText
    {
    }

    /// <summary>
    /// A dialog's footer band (spec 2.8.16): a <see cref="CommandRow"/> on WindowBackground with a 1 epx Divider line at the
    /// top, padding 16 epx top and bottom and 24 left and right, outside the scrolling content, so it is always visible.
    /// </summary>
    internal class CommandBar : CommandRow
    {
        private bool _divider = true;

        public CommandBar(TypeRamp ramp) : base(ramp)
        {
            UiScale s = ramp.Scale;
            Padding = new Padding(s.Px(Metrics.PagePadding), s.Px(16), s.Px(Metrics.PagePadding), s.Px(16));
            ApplyTheme(ThemeManager.Current);
        }

        /// <summary>The 1 epx Divider line along the top (the main window's footer row shows it only while the page scrolls).</summary>
        public bool ShowDivider
        {
            get => _divider;
            set
            {
                if (_divider == value) return;
                _divider = value;
                Invalidate();
            }
        }

        public override void ApplyTheme(Theme theme)
        {
            BackColor = theme.WindowBackground;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme t = ThemeManager.Current;
            using (var brush = new SolidBrush(t.WindowBackground)) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (!_divider) return;
            int line = Ramp.Scale.Stroke(1);
            using (var brush = new SolidBrush(t.Divider)) e.Graphics.FillRectangle(brush, 0, 0, ClientSize.Width, line);
        }
    }
}
