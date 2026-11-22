using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// The pieces every redesigned dialog is built from (spec 6.1): the heading, body text, bullets, rows of links and the
    /// spoken message. Gaps that the spec gives between a label and a link, or between links, are measured between what
    /// shows (letters or the external-link glyph): a link's box has a 4 epx padding, and GDI leaves a blank margin around a
    /// label's and a link's text (<see cref="GdiText"/>).
    /// </summary>
    internal static class DialogParts
    {
        /// <summary>A small dialog's content width: 432 epx (480 minus the 24 epx padding on both sides).</summary>
        public const int ContentWidth = Metrics.DialogWidth - 2 * Metrics.PagePadding;

        /// <summary>A dialog's heading: Subtitle, TextPrimary, wrapping in <paramref name="widthEpx"/>.</summary>
        public static ThemedLabel Heading(TypeRamp ramp, string text, int widthEpx = ContentWidth) =>
            new ThemedLabel(ramp, text, TextStyle.Subtitle, TextRole.Primary, widthEpx);

        /// <summary>Body text, <paramref name="topEpx"/> below the row above it.</summary>
        public static ThemedLabel Text(TypeRamp ramp, string text, int topEpx, TextRole role = TextRole.Primary, int widthEpx = ContentWidth,
            TextStyle style = TextStyle.Body)
        {
            var label = new ThemedLabel(ramp, text, style, role, widthEpx);
            label.Margin = new Padding(0, ramp.Scale.Px(topEpx), 0, 0);
            return label;
        }

        /// <summary>
        /// The text a screen reader hears with the first-focused control: the heading, then the body. A heading has no period of
        /// its own; one that ends with a question mark keeps it.
        /// </summary>
        public static string Spoken(string heading, params string[] parts)
        {
            string text = heading;
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                text = EndsSentence(text) ? text + " " + part : text + ". " + part;
            }
            return text;
        }

        private static bool EndsSentence(string text)
        {
            if (text.Length == 0) return true;
            char last = text[text.Length - 1];
            return last == '.' || last == '?' || last == '!' || last == ':';
        }

        /// <summary>
        /// A wrapping row of links (About) whose visible parts are <paramref name="gapEpx"/> apart; the first link's text lines
        /// up with text above it only if the row is placed 4 epx left of the column. Each gap is the right margin of the part
        /// before it, so a line the row wraps onto starts at the row's left edge.
        /// </summary>
        public static ThemedFlow LinkRow(TypeRamp ramp, int gapEpx, params Control[] parts)
        {
            var row = new ThemedFlow();
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].Margin = i + 1 < parts.Length ? new Padding(0, 0, InkGap(ramp, parts[i], parts[i + 1], gapEpx), 0) : Padding.Empty;
                row.Controls.Add(parts[i]);
            }
            return row;
        }

        /// <summary>
        /// The margin that puts <paramref name="gapEpx"/> between what <paramref name="before"/> shows last (its letters, or a
        /// link's external glyph) and what <paramref name="after"/> shows first. A label in the same row gets the link's top
        /// padding, so both texts sit on one line.
        /// </summary>
        public static int InkGap(TypeRamp ramp, Control before, Control after, int gapEpx)
        {
            UiScale s = ramp.Scale;
            int trailing;
            if (before is ThemedLink link && link.External) trailing = s.Px(4);
            else if (before is ThemedLink) trailing = before.Padding.Right + GdiText.RightMargin(before.Font);
            else trailing = GdiText.RightMargin(before.Font);
            int leading = after is ThemedLink ? after.Padding.Left + GdiText.LeftMargin(after.Font) : GdiText.LeftMargin(after.Font);
            return Math.Max(0, s.Px(gapEpx) - trailing - leading);
        }

        /// <summary>A label beside links: moved down by the link's top padding, so the texts share a line.</summary>
        public static void AlignWithLinks(ThemedLabel label, ThemedLink link)
        {
            Padding m = label.Margin;
            label.Margin = new Padding(m.Left, link.Padding.Top, m.Right, m.Bottom);
        }

        /// <summary>
        /// A bullet list (the Strict dialogs): a 20 epx column with the bullet in TextSecondary, then the item text wrapping in
        /// the rest of <paramref name="widthEpx"/>; the first item <paramref name="topEpx"/> below the row above, the others 8
        /// epx apart. With large text the column is as wide as the bullet's label needs (narrower, the bullet isn't drawn).
        /// </summary>
        public static ThemedTable Bullets(TypeRamp ramp, string[] items, int topEpx, int widthEpx, out ThemedLabel[] labels)
        {
            UiScale s = ramp.Scale;
            int column = Math.Max(s.Px(20), TextRenderer.MeasureText(Strings.Bullet, ramp.Body).Width);
            var table = new ThemedTable(2) { Margin = new Padding(0, s.Px(topEpx), 0, 0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            labels = new ThemedLabel[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                int top = i == 0 ? 0 : s.Px(Metrics.ButtonGap);
                var bullet = new ThemedLabel(ramp, Strings.Bullet, TextStyle.Body, TextRole.Secondary) { Margin = new Padding(0, top, 0, 0) };
                var text = new ThemedLabel(ramp, items[i], TextStyle.Body, TextRole.Primary)
                {
                    Margin = new Padding(0, top, 0, 0),
                    MaximumSize = new Size(Math.Max(1, s.Px(widthEpx) - column), 0),
                };
                labels[i] = text;
                Ui.AddRow(table, bullet, text);
            }
            return table;
        }
    }
}
