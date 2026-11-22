using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// A line of text and links that wraps (the block list line, the first run's limit line, the Fix dialog's report line;
    /// spec 2.8.4, 3.7, 4.2, 5.2): the parts follow each other left to right, each part's right margin being the gap after it
    /// (<see cref="DialogParts.InkGap"/>), and a label's top margin lining its text up with the links' text. Every line's
    /// first letter is on one edge, <see cref="Inset"/> from the flow's left: a link that starts a line sits its padding and
    /// GDI's margin further left than a label, so its letters line up with the text above it. A part after a label that
    /// wraps follows the label's last line when it fits there (the label then leaves that part's box to it); otherwise it
    /// starts the next line. Screen readers read the parts in their order; the flow itself is named by its first sentence.
    /// </summary>
    internal sealed class TextFlow : Panel, IThemed, IWrapsText
    {
        private readonly List<Control> _parts = new List<Control>();
        // The labels whose window region leaves a box to the part after their last line: that box and the label's bounds.
        private readonly Dictionary<Control, (Rectangle Box, Rectangle Bounds)> _cuts = new Dictionary<Control, (Rectangle, Rectangle)>();

        public TextFlow(params Control[] parts)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            foreach (Control p in parts)
            {
                if (p == null) continue;
                if (IsLabel(p))
                {
                    // The flow gives each label its room when it measures it and its size when it places it (Measure,
                    // Arrange): a label that sizes itself takes its unwrapped size whatever bounds it is given, and the flow
                    // would cut its last words off at its right edge.
                    p.MaximumSize = Size.Empty;
                    p.AutoSize = false;
                }
                // A new text or font lays the flow out again (a label that doesn't size itself doesn't ask for it).
                p.TextChanged += OnPartChanged;
                p.FontChanged += OnPartChanged;
                _parts.Add(p);
                Controls.Add(p);
            }
            // Named by its own sentence (the text its links follow), which a screen reader reads as the links' context: UI
            // Automation names a pane without a name (an empty one counts as none) after the nearest label before it, an
            // unrelated text here ("Blocking level" for the block list line).
            if (_parts.Count > 0 && IsLabel(_parts[0]))
            {
                Control sentence = _parts[0];
                sentence.TextChanged += (o, e) => AccessibleName = sentence.Text ?? string.Empty;
                AccessibleName = sentence.Text ?? string.Empty;
            }
            else
            {
                AccessibleName = string.Empty;
            }
        }

        /// <summary>The parts in reading order.</summary>
        public IReadOnlyList<Control> Parts => _parts;

        /// <summary>
        /// From the flow's left edge to every line's first letter: the largest lead of its parts (a link's padding and GDI's
        /// margin). Its owner places the flow this far left of the text column.
        /// </summary>
        public int Inset
        {
            get
            {
                int inset = 0;
                foreach (Control p in _parts) inset = Math.Max(inset, Lead(p));
                return inset;
            }
        }

        /// <summary>From a part's left edge to its first letter: a link's padding and GDI's margin; a label's margin (none when tight).</summary>
        internal static int Lead(Control c) => c is LinkLabel ? c.Padding.Left + GdiText.LeftMargin(c.Font) : GdiText.LeftMarginOf(c);

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
            Arrange(Math.Max(ClientSize.Width, 1), apply: true);
        }

        private static bool IsLabel(Control c) => c is Label && !(c is LinkLabel);

        private Size Arrange(int width, bool apply)
        {
            int inset = Inset;
            int lineTop = 0, bottom = 0, right = 0;
            // The next part's left edge on the current line; -1 at a line's start.
            int x = -1;
            // A label that wrapped, whose last line the next part may follow.
            Control? wrapped = null;
            var cuts = new Dictionary<Control, Rectangle>();
            foreach (Control c in _parts)
            {
                if (!LayoutUtil.IsShown(c)) continue;
                int start = inset - Lead(c);
                Size size;
                if (IsLabel(c))
                {
                    int full = Math.Max(1, width - start);
                    Size whole = Measure(c, full);
                    bool oneLine = whole.Height < 2 * c.Font.Height;
                    // Not at a line's start: it follows the part before only if it fits there whole.
                    if (x >= 0 && !(oneLine && x + whole.Width <= width))
                    {
                        lineTop = bottom;
                        x = -1;
                        wrapped = null;
                    }
                    int left = x < 0 ? start : x;
                    int room = Math.Max(1, width - left);
                    size = Measure(c, room);
                    int top = lineTop + c.Margin.Top;
                    if (apply) c.SetBounds(left, top, Math.Min(room, size.Width), size.Height);
                    bottom = Math.Max(bottom, top + size.Height);
                    right = Math.Max(right, left + Math.Min(room, size.Width));
                    if (size.Height >= 2 * c.Font.Height && LastLine(c, room, out int lines, out int lastWidth) && lines > 1)
                    {
                        // The next part may follow its last line, as the next word of the sentence would.
                        lineTop = top + (lines - 1) * c.Font.Height - c.Margin.Top;
                        x = left + GdiText.LeftMarginOf(c) + lastWidth + GdiText.RightMarginOf(c) + c.Margin.Right;
                        wrapped = c;
                    }
                    else if (size.Height >= 2 * c.Font.Height)
                    {
                        // Wrapped, but its last line isn't known: the next part starts a line of its own.
                        lineTop = bottom;
                        x = -1;
                        wrapped = null;
                    }
                    else
                    {
                        x = left + size.Width + c.Margin.Right;
                        wrapped = null;
                    }
                    continue;
                }

                size = c.GetPreferredSize(Size.Empty);
                if (x >= 0 && x + size.Width > width)
                {
                    lineTop = bottom;
                    x = -1;
                    wrapped = null;
                }
                int partLeft = x < 0 ? start : x;
                var box = new Rectangle(partLeft, lineTop + c.Margin.Top, size.Width, size.Height);
                if (apply)
                {
                    c.SetBounds(box.X, box.Y, box.Width, box.Height);
                    // After a wrapped label's last line: the label leaves this box to the part (its window would cover it).
                    if (wrapped != null)
                    {
                        Rectangle overlap = Rectangle.Intersect(wrapped.Bounds, box);
                        if (!overlap.IsEmpty) cuts[wrapped] = overlap;
                    }
                }
                bottom = Math.Max(bottom, box.Bottom);
                right = Math.Max(right, box.Right);
                x = box.Right + c.Margin.Right;
                wrapped = null;
            }
            if (apply) ApplyCuts(cuts);
            return new Size(Math.Min(width, Math.Max(0, right)), bottom);
        }

        /// <summary>
        /// A label's size wrapping in <paramref name="room"/> pixels. The flow never limits a label through its maximum size
        /// (it clears it): setting that lays the flow out again from inside a measurement, with the width it had before, which
        /// then sticks.
        /// </summary>
        private static Size Measure(Control label, int room) => label.GetPreferredSize(new Size(Math.Max(1, room), 0));

        /// <summary>
        /// How many lines a label's text takes in <paramref name="room"/> pixels and how wide its last line's text is, by
        /// breaking at spaces as GDI does; false when that doesn't match the label's own measure (then nothing follows it).
        /// </summary>
        private static bool LastLine(Control label, int room, out int lines, out int lastWidth)
        {
            lines = 0;
            lastWidth = 0;
            string text = label.Text ?? string.Empty;
            if (text.Length == 0) return false;
            int textRoom = room - label.Padding.Horizontal - GdiText.LeftMarginOf(label) - GdiText.RightMarginOf(label);
            if (textRoom <= 0) return false;
            TextFormatFlags flags = TextFormatFlags.SingleLine | (label is Label l && !l.UseMnemonic ? TextFormatFlags.NoPrefix : TextFormatFlags.Default);
            string line = string.Empty;
            lines = 1;
            foreach (string word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length == 0 || GdiText.Measure(candidate, label.Font, flags).Width <= textRoom)
                {
                    line = candidate;
                    continue;
                }
                lines++;
                line = word;
            }
            lastWidth = GdiText.Measure(line, label.Font, flags).Width;
            // A word wider than the room would be broken inside: not this simple model.
            if (lastWidth > textRoom) return false;
            int measured = GdiText.Measure(text, label.Font, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | (flags & TextFormatFlags.NoPrefix),
                new Size(textRoom, int.MaxValue)).Height;
            return measured / Math.Max(1, label.Font.Height) == lines;
        }

        /// <summary>Gives each label that has a part after its last line a window region without that part's box.</summary>
        private void ApplyCuts(Dictionary<Control, Rectangle> cuts)
        {
            foreach (Control c in _parts)
            {
                bool cut = cuts.TryGetValue(c, out Rectangle box);
                bool had = _cuts.TryGetValue(c, out (Rectangle Box, Rectangle Bounds) old);
                if (cut && had && old.Box == box && old.Bounds == c.Bounds && c.Region != null) continue;
                if (!cut && !had) continue;
                Region? previous = c.Region;
                if (cut)
                {
                    var region = new Region(new Rectangle(Point.Empty, c.Size));
                    region.Exclude(new Rectangle(box.X - c.Left, box.Y - c.Top, box.Width, box.Height));
                    c.Region = region;
                    _cuts[c] = (box, c.Bounds);
                }
                else
                {
                    c.Region = null;
                    _cuts.Remove(c);
                }
                previous?.Dispose();
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: whether a label leaves a box to the part after its last line.</summary>
        internal bool PreviewCuts(Control label) => _cuts.ContainsKey(label);
#endif
    }
}
