using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// The first run (spec 4), shown in the main window in place of its page on a Windows user's first launch while
    /// protection is off: the app icon, the title, three facts (what changes, why Windows asks, how to undo), the older-list
    /// line when the block-ads.ps1 list is there, the limit line with "Why?", and the trust line. Its two buttons, Turn on
    /// protection and Not now, go in the window's footer slot, which stays visible when the text scrolls.
    /// </summary>
    internal sealed class FirstRunPanel : ThemedTable
    {
        private const int ContentWidth = Metrics.MainClientWidth - 2 * Metrics.PagePadding;
        private const int FactIndent = 20 + 12;

        public FirstRunPanel(TypeRamp ramp, UiContext ctx, HostsStatus status, int widthPx) : base(1)
        {
            if (ramp == null) throw new ArgumentNullException(nameof(ramp));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (status == null) throw new ArgumentNullException(nameof(status));
            UiScale s = ramp.Scale;
            int pad = s.Px(Metrics.PagePadding);
            WhyLink = new ThemedLink(ramp, Strings.FirstRunWhyLink);
            // Every row's text starts on the 24 epx edge, where the icon, the fact glyphs and the buttons are (4.2): the labels
            // are tight (no GDI margin before their letters). The panel's padding leaves room left of that edge for a link
            // that starts a line (its padding and margin go further left, so its letters line up too); every other row is
            // indented by that room.
            int reach = TextFlow.Lead(WhyLink);
            Padding = new Padding(Math.Max(0, pad - reach), pad, pad, 0);
            MinimumSize = new Size(widthPx, 0);
            MaximumSize = new Size(widthPx, 0);
            int gap = s.Px(16);
            var spoken = new List<string>();

            Icon = new AppIconImage(ctx.ExePath) { Margin = new Padding(reach, 0, 0, 0) };
            Ui.AddRow(this, Icon);
            Title = new ThemedLabel(ramp, Strings.FirstRunTitle, TextStyle.Title, TextRole.Primary, ContentWidth) { Tight = true, Margin = new Padding(reach, gap, 0, 0) };
            Ui.AddRow(this, Title);
            spoken.Add(Strings.FirstRunTitle);

            SignatureState signature = ctx.Signature.Value;
            string fact2 = signature.Kind == SignatureKind.Signed && !string.IsNullOrEmpty(signature.Publisher)
                ? Strings.FirstRunFact2Signed(signature.Publisher!)
                : Strings.FirstRunFact2Unsigned;
            var facts = new (Glyph Glyph, string Title, string Body)[]
            {
                (Glyph.Globe, Strings.FirstRunFact1Title, Strings.FirstRunFact1Body(ctx.Lists.StandardCount)),
                (Glyph.Lock, Strings.FirstRunFact2Title, fact2),
                (Glyph.Undo, Strings.FirstRunFact3Title, Strings.FirstRunFact3Body),
            };
            var bodies = new List<ThemedLabel>();
            foreach ((Glyph glyph, string title, string body) in facts)
            {
                // The glyph is decorative: the title label paints it (20 epx, 12 epx gap); the body lines up with the title text.
                var titleLabel = new ThemedLabel(ramp, title, TextStyle.BodyStrong, TextRole.Primary, ContentWidth)
                {
                    LeadingGlyph = glyph,
                    LeadingGlyphSize = 20,
                    LeadingGlyphGap = 12,
                    LeadingGlyphColor = ThemeToken.TextSecondary,
                    Tight = true,
                    Margin = new Padding(reach, gap, 0, 0),
                };
                var bodyLabel = new ThemedLabel(ramp, body, TextStyle.Body, TextRole.Secondary, Metrics.MaxTextWidth)
                {
                    Tight = true,
                    Margin = new Padding(reach + s.Px(FactIndent), s.Px(4), 0, 0),
                };
                Ui.AddRow(this, titleLabel);
                Ui.AddRow(this, bodyLabel);
                bodies.Add(bodyLabel);
                spoken.Add(title);
                spoken.Add(body);
            }
            FactBodies = bodies;

            if (status.HasLegacyBlock)
            {
                Legacy = new ThemedLabel(ramp, Strings.FirstRunLegacy, TextStyle.Body, TextRole.Primary, ContentWidth) { Tight = true, Margin = new Padding(reach, gap, 0, 0) };
                Ui.AddRow(this, Legacy);
                spoken.Add(Strings.FirstRunLegacy);
            }

            // The limit, then "Why?" on the same line after it (the link's padding and margin are about two spaces); at large
            // text, when the sentence wraps, "Why?" follows its last line, or starts the next line with its letters on the
            // edge. "Why?" opens How it works at "What it can't block" (no browser).
            Limit = new ThemedLabel(ramp, Strings.FirstRunLimit, TextStyle.Body, TextRole.Primary) { Tight = true };
            DialogParts.AlignWithLinks(Limit, WhyLink);
            LimitLine = new TextFlow(Limit, WhyLink) { MaximumSize = new Size(s.Px(ContentWidth) + reach, 0), Margin = new Padding(0, gap, 0, 0) };
            Ui.AddRow(this, LimitLine);
            spoken.Add(Strings.FirstRunLimit);

            Trust = new ThemedLabel(ramp, Strings.FirstRunTrust, TextStyle.Body, TextRole.Secondary, ContentWidth) { Tight = true, Margin = new Padding(reach, gap, 0, 0) };
            Ui.AddRow(this, Trust);
            spoken.Add(Strings.FirstRunTrust);

            SpokenText = Join(spoken);
            // The footer slot's buttons (the main window places them). Narrator reads the whole text with the focused button.
            TurnOnButton = new ThemedButton(ramp, Strings.FirstRunTurnOnButton, ButtonStyle.Accent)
            {
                Shield = true,
                // Enter never turns protection on (K2, D1); Space, the access key and a click do.
                SwallowsEnter = true,
                ContextDescription = SpokenText,
            };
            NotNowButton = new ThemedButton(ramp, Strings.FirstRunNotNowButton);
        }

        public AppIconImage Icon { get; }
        public ThemedLabel Title { get; }
        public IReadOnlyList<ThemedLabel> FactBodies { get; }
        /// <summary>The older-list line (null without the block-ads.ps1 list).</summary>
        public ThemedLabel? Legacy { get; }
        /// <summary>The limit sentence ("Video ads on YouTube and Twitch can't be blocked this way.").</summary>
        public ThemedLabel Limit { get; }
        /// <summary>The limit sentence and "Why?" after it.</summary>
        public TextFlow LimitLine { get; }
        public ThemedLink WhyLink { get; }
        public ThemedLabel Trust { get; }
        /// <summary>Accent, the shield, initial focus: marks the first run done and turns protection on at Standard.</summary>
        public ThemedButton TurnOnButton { get; }
        /// <summary>Esc while the first run shows: marks it done and shows the page.</summary>
        public ThemedButton NotNowButton { get; }

        /// <summary>The whole visible text in reading order, as Turn on protection's description (spec 4.4).</summary>
        public string SpokenText { get; }

        /// <summary>The lines joined with a space, each ending with its own period (or one added where a line has none).</summary>
        private static string Join(IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(line);
                char last = line.Length > 0 ? line[line.Length - 1] : '.';
                if (last != '.' && last != '?' && last != '!') sb.Append('.');
            }
            return sb.ToString();
        }
    }
}
