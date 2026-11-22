using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>What a level card's right side shows (spec 3.6).</summary>
    internal enum LevelCardState
    {
        /// <summary>A button: "Use Standard" or "Use Strict…".</summary>
        None,
        /// <summary>The level in the hosts file (On).</summary>
        InUse,
        /// <summary>The level Turn on will use (Off, Older list, Removed, Can't read, Refused).</summary>
        Selected,
    }

    /// <summary>One level card's right side: an indicator, or a button with its shield and whether it can be clicked.</summary>
    internal sealed class CardView
    {
        public CardView(Level level, LevelCardState state, bool shield = false, bool enabled = false)
        {
            Level = level;
            State = state;
            Shield = state == LevelCardState.None && shield;
            Enabled = state == LevelCardState.None && enabled;
        }

        public Level Level { get; }
        public LevelCardState State { get; }
        /// <summary>The right side is the card's button (otherwise the "In use" or "Selected" indicator).</summary>
        public bool ShowsButton => State == LevelCardState.None;
        /// <summary>The button's click starts an elevated change ("Use Standard" while On).</summary>
        public bool Shield { get; }
        public bool Enabled { get; }
        public string ButtonText => Level == Level.Strict ? Strings.UseStrictCardButton : Strings.UseStandardCardButton;
        public string IndicatorText => State == LevelCardState.InUse ? Strings.InUseIndicator : Strings.SelectedIndicator;
        /// <summary>The card's group name ("Standard, recommended, in use").</summary>
        public string Name => Strings.LevelCardName(Level, State);
    }

    /// <summary>The two level cards for a status (spec 3.6): a pure function, so the tests pin the whole table.</summary>
    internal static class LevelUi
    {
        public static (CardView Standard, CardView Strict, bool ShowNote) For(HostsStatus s, bool canStartChange)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            switch (s.State)
            {
                case ProtectionState.On:
                {
                    // The level in the file is in use; the other card switches to it (Use Standard prompts at once, Use
                    // Strict… opens the confirmation, whose Use Strict carries the shield).
                    Level inUse = s.BlockLevel ?? s.SelectedLevel;
                    return (Card(Level.Standard, inUse, LevelCardState.InUse, shield: true, canStartChange),
                        Card(Level.Strict, inUse, LevelCardState.InUse, shield: false, canStartChange), false);
                }
                case ProtectionState.Off:
                case ProtectionState.OlderList:
                case ProtectionState.Removed:
                    // Only a preference for the next Turn on: no prompt, no shield.
                    return (Card(Level.Standard, s.SelectedLevel, LevelCardState.Selected, shield: false, canStartChange),
                        Card(Level.Strict, s.SelectedLevel, LevelCardState.Selected, shield: false, canStartChange), false);
                default:
                    // Can't read, Refused: the level can't change now; the note under the heading says when it can.
                    return (Card(Level.Standard, s.SelectedLevel, LevelCardState.Selected, shield: false, enabled: false),
                        Card(Level.Strict, s.SelectedLevel, LevelCardState.Selected, shield: false, enabled: false), true);
            }
        }

        private static CardView Card(Level card, Level current, LevelCardState indicator, bool shield, bool enabled) =>
            card == current ? new CardView(card, indicator) : new CardView(card, LevelCardState.None, shield, enabled);
    }

    /// <summary>
    /// A card title with an optional tag drawn after it (the Standard card's "Recommended" pill, spec 3.6): Body Strong text,
    /// then 8 epx, then the pill, vertically centered on the title. The pill is drawn, not a control; its text is in this
    /// label's accessible name.
    /// </summary>
    internal sealed class CardTitleLabel : ThemedLabel
    {
        private readonly string? _tag;

        public CardTitleLabel(TypeRamp ramp, string title, string? tag) : base(ramp, title, TextStyle.BodyStrong)
        {
            _tag = tag;
            if (!string.IsNullOrEmpty(tag))
            {
                Size pill = Pill.Measure(ramp, tag!);
                Padding = new Padding(0, 0, ramp.Scale.Px(8) + pill.Width, 0);
                MinimumSize = new Size(0, Math.Max(MinimumSize.Height, pill.Height));
            }
        }

        /// <summary>The tag's text ("Recommended"), or null.</summary>
        public string? PillText => _tag;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(_tag)) return;
            // 8 epx after the title's last letter: the label draws its text after GDI's blank margin.
            int text = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            Size pill = Pill.Measure(Ramp, _tag!);
            int line = Math.Max(Font.Height, Ramp.LineHeight(TextStyle.BodyStrong));
            var bounds = new Rectangle(Padding.Left + GdiText.LeftMargin(Font) + text + Ramp.Scale.Px(8), Math.Max(0, (line - pill.Height) / 2), pill.Width, pill.Height);
            Pill.Draw(e.Graphics, Ramp, PillKind.Accent, _tag!, bounds, selectedRow: false, ThemeManager.Current);
        }
    }

    /// <summary>
    /// The blank margins GDI leaves around a label's or link's text (TextRenderer's default glyph-overhang padding, which
    /// WinForms labels and links use): ceil(height / 6) before the text and ceil(height / 6 × 1.5) after it. Places that line
    /// something up with the letters themselves (the pill after the Standard title, the gap before "Check for a newer
    /// version") add them in.
    /// </summary>
    internal static class GdiText
    {
        public static int LeftMargin(Font font) => (int)Math.Ceiling(font.Height / 6f);

        public static int RightMargin(Font font) => (int)Math.Ceiling(font.Height / 6f * 1.5f);

        /// <summary>The blank margin before a control's text: none for a tight label, GDI's for other labels and links.</summary>
        public static int LeftMarginOf(Control c) => c is ThemedLabel label && label.Tight ? 0 : LeftMargin(c.Font);

        /// <summary>The blank margin after a control's text: none for a tight label, GDI's for other labels and links.</summary>
        public static int RightMarginOf(Control c) => c is ThemedLabel label && label.Tight ? 0 : RightMargin(c.Font);

        /// <summary>
        /// The size of <paramref name="text"/> as text drawn with NoPadding takes it (no blank margins), measured on a screen
        /// device context: the TextRenderer.MeasureText overloads without one ignore NoPadding in .NET Framework, so a width
        /// measured there is GDI's margins (9 px for Body at 96 DPI) wider than what such text draws.
        /// </summary>
        /// <param name="flags">Formatting flags; NoPadding is always added.</param>
        /// <param name="proposed">The room (a width to wrap in with WordBreak); unbounded by default.</param>
        public static Size Measure(string? text, Font font, TextFormatFlags flags, Size? proposed = null)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            var key = new MeasureKey(text!, font, flags | TextFormatFlags.NoPadding, proposed ?? new Size(int.MaxValue, int.MaxValue));
            // Layout asks for the same texts again and again, and a device context per measure is slow: remembered per thread
            // (the window's), forgotten in bulk now and then so changing texts can't make it grow without end.
            Dictionary<MeasureKey, Size> cache = _measured ??= new Dictionary<MeasureKey, Size>();
            if (cache.TryGetValue(key, out Size known)) return known;
            Size size;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                size = TextRenderer.MeasureText(g, key.Text, font, key.Proposed, key.Flags);
            }
            if (cache.Count >= MaxMeasured) cache.Clear();
            cache[key] = size;
            return size;
        }

        private const int MaxMeasured = 2048;

        [ThreadStatic]
        private static Dictionary<MeasureKey, Size>? _measured;

        /// <summary>One measurement: the text, the font (by its name, size, style and unit, never the object), the flags and the room.</summary>
        private readonly struct MeasureKey : IEquatable<MeasureKey>
        {
            public MeasureKey(string text, Font font, TextFormatFlags flags, Size proposed)
            {
                Text = text;
                FontName = font.Name;
                FontSize = font.Size;
                FontStyle = font.Style;
                FontUnit = font.Unit;
                Flags = flags;
                Proposed = proposed;
            }

            public string Text { get; }
            public string FontName { get; }
            public float FontSize { get; }
            public FontStyle FontStyle { get; }
            public GraphicsUnit FontUnit { get; }
            public TextFormatFlags Flags { get; }
            public Size Proposed { get; }

            public bool Equals(MeasureKey other) =>
                string.Equals(Text, other.Text, StringComparison.Ordinal) && string.Equals(FontName, other.FontName, StringComparison.Ordinal) &&
                FontSize == other.FontSize && FontStyle == other.FontStyle && FontUnit == other.FontUnit && Flags == other.Flags && Proposed == other.Proposed;

            public override bool Equals(object? obj) => obj is MeasureKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = StringComparer.Ordinal.GetHashCode(Text);
                    h = h * 31 + StringComparer.Ordinal.GetHashCode(FontName);
                    h = h * 31 + FontSize.GetHashCode();
                    h = h * 31 + (int)FontStyle;
                    h = h * 31 + (int)Flags;
                    return h * 31 + Proposed.GetHashCode();
                }
            }
        }
    }

    /// <summary>
    /// One blocking-level card (spec 3.6): on the left the title (Standard with its "Recommended" tag), 4 epx, the
    /// description and, on the Strict card, 4 epx and "What Strict changes"; on the right, vertically centered in a column
    /// as wide on both cards, "In use", "Selected" or the card's button. When the left column would be narrower than
    /// 240 epx or than the Standard title row (large text) the card stacks: the right side goes 8 epx below the left column,
    /// left-aligned. A group named
    /// for screen readers ("Standard, recommended, in use"); the button's description is the card's description.
    /// </summary>
    internal sealed class LevelCard : CardPanel
    {
        private const int MinLeftColumn = 240;
        private readonly TypeRamp _ramp;
        private readonly int _width;
        private int _rightWidth, _minLeftWidth;

        public LevelCard(TypeRamp ramp, Level level, string description, int widthPx) : base(Metrics.CardPadding, withContent: false)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            Level = level;
            _width = widthPx;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            SetFixedWidth(widthPx);
            UseMinimumHeight();
            Title = new CardTitleLabel(ramp, Strings.LevelName(level), level == Level.Standard ? Strings.RecommendedTag : null);
            if (level == Level.Standard) Title.AccessibleName = Strings.LevelName(level) + Strings.RecommendedSuffix;
            Description = new ThemedLabel(ramp, description, TextStyle.Body, TextRole.Secondary);
            Button = new ThemedButton(ramp, level == Level.Strict ? Strings.UseStrictCardButton : Strings.UseStandardCardButton)
            {
                // Enter never switches the level (K2); Space, the access key and a click do.
                SwallowsEnter = true,
                ContextDescription = description,
                TabIndex = 0,
            };
            Indicator = CreateIndicator(ramp, Strings.InUseIndicator);
            Indicator.Visible = false;
            Controls.Add(Title);
            Controls.Add(Description);
            Controls.Add(Button);
            Controls.Add(Indicator);
            if (level == Level.Strict)
            {
                // Tab order: the card's button, then "What Strict changes" (spec 3.8).
                Link = new ThemedLink(ramp, Strings.WhatStrictChangesLink) { TabIndex = 1 };
                Controls.Add(Link);
            }
            AccessibleName = Strings.LevelCardName(level, LevelCardState.None);
            ApplyTheme(ThemeManager.Current);
        }

        public Level Level { get; }
        public CardTitleLabel Title { get; }
        public ThemedLabel Description { get; }
        /// <summary>"Use Standard" or "Use Strict…" (hidden while the indicator shows).</summary>
        public ThemedButton Button { get; }
        /// <summary>"In use" or "Selected" with the check glyph (hidden while the button shows).</summary>
        public ThemedLabel Indicator { get; }
        /// <summary>The Strict card's "What Strict changes" (null on the Standard card).</summary>
        public ThemedLink? Link { get; }

        /// <summary>What the card shows now.</summary>
        public CardView? View { get; private set; }

        /// <summary>The right column's width in pixels, the same on both cards (0: this card's own widest right side).</summary>
        public int RightWidth
        {
            get => _rightWidth;
            set
            {
                if (_rightWidth == value) return;
                _rightWidth = value;
                Relayout();
            }
        }

        /// <summary>
        /// The right column's width for both cards (spec 3.6): at least 120 epx and as wide as anything either card can show
        /// there ("Use Standard" with the shield, "Use Strict…", "In use", "Selected"), so both left columns are equally wide.
        /// </summary>
        public static int RightColumnWidth(TypeRamp ramp)
        {
            int w = ramp.Scale.Px(Metrics.LevelRightMinWidth);
            using (var standard = new ThemedButton(ramp, Strings.UseStandardCardButton) { Shield = true })
                w = Math.Max(w, standard.GetPreferredSize(Size.Empty).Width);
            using (var strict = new ThemedButton(ramp, Strings.UseStrictCardButton))
                w = Math.Max(w, strict.GetPreferredSize(Size.Empty).Width);
            foreach (string text in new[] { Strings.InUseIndicator, Strings.SelectedIndicator })
            {
                using (ThemedLabel indicator = CreateIndicator(ramp, text)) w = Math.Max(w, indicator.GetPreferredSize(Size.Empty).Width);
            }
            return w;
        }

        /// <summary>
        /// The narrowest left column for both cards: 240 epx (spec 3.6), and at least as wide as the widest title row (the
        /// Standard title with its "Recommended" tag), which can't wrap. Narrower, a card stacks. At 225% text the left column
        /// came out 241 px beside "Use Standard", which left the title 43 px beside its tag: it broke into a letter or two per
        /// line, and the description started some 300 px lower.
        /// </summary>
        public static int MinLeftColumnWidth(TypeRamp ramp)
        {
            int w = ramp.Scale.Px(MinLeftColumn);
            using (var title = new CardTitleLabel(ramp, Strings.LevelName(Level.Standard), Strings.RecommendedTag))
                w = Math.Max(w, title.GetPreferredSize(Size.Empty).Width);
            using (var title = new CardTitleLabel(ramp, Strings.LevelName(Level.Strict), null))
                w = Math.Max(w, title.GetPreferredSize(Size.Empty).Width);
            return w;
        }

        private static ThemedLabel CreateIndicator(TypeRamp ramp, string text) => new ThemedLabel(ramp, text)
        {
            LeadingGlyph = Glyph.CheckMark,
            LeadingGlyphColor = ThemeToken.AccentText,
            LeadingGlyphSize = 16,
            LeadingGlyphGap = 8,
        };

        /// <summary>Shows <paramref name="view"/>: the button or the indicator, the shield, the group name.</summary>
        public void Show(CardView view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            SuspendLayout();
            try
            {
                Button.Visible = view.ShowsButton;
                Button.Shield = view.Shield;
                Button.Enabled = view.Enabled;
                Indicator.Visible = !view.ShowsButton;
                Indicator.Text = view.IndicatorText;
                AccessibleName = view.Name;
            }
            finally
            {
                ResumeLayout(false);
            }
            Relayout();
        }

        /// <summary>True when the last layout stacked the card (large text).</summary>
        internal bool IsStacked { get; private set; }

        private void Relayout()
        {
            Parent?.PerformLayout(this, nameof(Bounds));
            PerformLayout();
            Invalidate();
        }

        // ---------------------------------------------------------------- layout

        public override Size GetPreferredSize(Size proposedSize) =>
            new Size(_width, Math.Max(_ramp.Scale.Px(Metrics.CardMinHeight), Arrange(apply: false)));

        protected override void OnLayout(LayoutEventArgs levent)
        {
            // The base constructor's padding lays out before this card's parts exist.
            if (ClientSize.Width <= 0 || Indicator == null) return;
            SuspendLayout();
            try
            {
                Arrange(apply: true);
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        /// <summary>Lays the card out (with <paramref name="apply"/>: sets the bounds); returns its height.</summary>
        private int Arrange(bool apply)
        {
            if (Indicator == null) return 0;
            UiScale s = _ramp.Scale;
            int pad = s.Px(Metrics.CardPadding);
            int inner = _width - 2 * pad;
            if (_rightWidth <= 0) _rightWidth = RightColumnWidth(_ramp);
            if (_minLeftWidth <= 0) _minLeftWidth = MinLeftColumnWidth(_ramp);
            int right = _rightWidth;
            int left = inner - s.Px(Metrics.TextToButton) - right;
            bool stacked = left < _minLeftWidth;
            if (stacked) left = inner;
            if (apply)
            {
                IsStacked = stacked;
                // Tab order is reading order (K6): side by side the button comes first (3.8); stacked, "What Strict changes"
                // sits above the button, so it comes first.
                if (Link != null)
                {
                    Link.TabIndex = stacked ? 0 : 1;
                    Button.TabIndex = stacked ? 1 : 0;
                }
            }

            Control side = LayoutUtil.IsShown(Button) ? (Control)Button : Indicator;
            Size sideSize = side.GetPreferredSize(Size.Empty);

            int y = 0;
            Size title = Title.GetPreferredSize(new Size(left, 0));
            if (apply) Title.SetBounds(pad, pad + y, Math.Min(left, title.Width), title.Height);
            y += title.Height + s.Px(4);
            Description.MaximumSize = new Size(left, 0);
            Size description = Description.GetPreferredSize(new Size(left, 0));
            if (apply) Description.SetBounds(pad, pad + y, Math.Min(left, description.Width), description.Height);
            y += description.Height;
            if (Link != null)
            {
                y += s.Px(4);
                Size link = Link.GetPreferredSize(Size.Empty);
                // The link's text lines up with the description (its box has a 4 epx padding before the text).
                if (apply) Link.SetBounds(pad - Link.Padding.Left, pad + y, link.Width, link.Height);
                y += link.Height;
            }
            int leftHeight = y;

            int content;
            if (stacked)
            {
                int top = leftHeight + s.Px(Metrics.HeadingGap);
                if (apply) side.SetBounds(pad, pad + top, sideSize.Width, sideSize.Height);
                content = top + sideSize.Height;
            }
            else
            {
                content = Math.Max(leftHeight, sideSize.Height);
                int minimum = s.Px(Metrics.CardMinHeight) - 2 * pad;
                int column = Math.Max(content, minimum);
                // Right-aligned in its column, vertically centered in the card.
                if (apply) side.SetBounds(pad + inner - sideSize.Width, pad + (column - sideSize.Height) / 2, sideSize.Width, sideSize.Height);
            }
            return pad + content + pad;
        }
    }
}
