#if DEBUG || QUIETHOSTS_UI_TESTS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// Every component in every state side by side (spec 2.9): the reviewers' quick check of the token table, rendered in
    /// every theme by --render-ui and built by the UI tests. Hover, pressed and focus are forced on each instance through
    /// <see cref="UiOverrides"/>. Debug and test builds only; its few labels are developer text, not app strings.
    /// </summary>
    internal sealed class GalleryForm : ThemedWindow
    {
        public const string WindowTitle = "Component gallery";
        private readonly List<(Control Control, Point Point, string What)> _samples = new List<(Control, Point, string)>();
        private readonly ThemedTable _root;

        public GalleryForm(UiContext ctx)
        {
            Text = WindowTitle;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // Larger than a screen at 200% scale: let Windows size it to its content.
            MaximumSize = new Size(16000, 16000);

            _root = new ThemedTable(2) { Fill = ThemeToken.WindowBackground, Padding = new Padding(Scale.Px(Metrics.PagePadding)) };
            ThemedTable left = Column(), right = Column();
            left.Margin = new Padding(0, 0, Scale.Px(Metrics.SectionGap), 0);
            Ui.AddRow(_root, left, right);

            Section(left, "Type ramp and text roles (primary, secondary, disabled, leading glyphs)", TypeSamples());
            Section(left, "Buttons: rest, hover, pressed, disabled, focused, shield, external", ButtonGrid());
            Section(left, "Links: rest, hover, disabled, focused, external", Row(
                Link(Strings.CheckForNewerVersionLink, ForcedState.None),
                Link(Strings.ViewLicenseLink, ForcedState.Hover),
                Link(Strings.OpenBackupFolderLink, ForcedState.None, enabled: false),
                Link(Strings.HowItWorksLink, ForcedState.Focused),
                Link(Strings.ProjectPageLink, ForcedState.None, external: true)));
            Section(left, "Check box: unchecked, checked, hover, disabled, disabled checked, focused", Row(
                Check(ForcedState.None, false, true),
                Check(ForcedState.None, true, true),
                Check(ForcedState.Hover, false, true),
                Check(ForcedState.None, false, false),
                Check(ForcedState.None, true, false),
                Check(ForcedState.Focused, true, true)));
            Section(left, "Search box: hint, text, focused, disabled", SearchBoxes());
            Section(left, "Progress ring and its static glyph (animations off)", Row(Ring(true), Ring(false)));
            Section(left, "Status shield, UAC shield (16, 20, 24, 32 px), focus ring, app icon", Row(Shields(), new UacShields(), new FocusSample(), AppIconCard(ctx)));
            Section(left, "Glyphs at 16, 20 and 24 epx", new GlyphSheet());

            Section(right, "Notices: error, warning, info, tip, success", Notices());
            var pills = new PillSheet(Ramp);
            _samples.Add((pills, pills.PillPoint(selected: false), "pill on a normal row"));
            _samples.Add((pills, pills.PillPoint(selected: true), "pill on a selected row"));
            Section(right, "Pills on a normal and a selected row", pills);
            Section(right, "List: hovered, selected, selected and focused rows", List(ctx));
            Section(right, "Command bar (a dialog's footer band): left text, right buttons", CommandBarSample());

            Controls.Add(_root);
            ApplyTheme(ThemeManager.Current);
            EndLayout();
        }

        /// <summary>
        /// Tests (T-A-12): points inside cards, bars, buttons, pills and the selected row, away from text and edges, in the
        /// control's client coordinates. In a simulated high-contrast pass each must be one of the palette's colors. The
        /// selected row's point needs the list's handle (rows are placed by the native list).
        /// </summary>
        internal IReadOnlyList<(Control Control, Point Point, string What)> SamplePoints
        {
            get
            {
                var points = new List<(Control, Point, string)>(_samples);
                ThemedListView? list = GalleryList;
                if (list != null && list.IsHandleCreated && list.Items.Count > 2)
                {
                    Rectangle row = list.Items[2].Bounds;
                    points.Add((list, new Point(row.X + Scale.Px(13), row.Y + Scale.Px(4)), "selected row"));
                }
                return points;
            }
        }

        /// <summary>The panel that holds everything (opaque), for a DrawToBitmap of the whole gallery.</summary>
        internal Control Root => _root;

        private ThemedTable Column() => new ThemedTable(1);

        private void Section(ThemedTable column, string title, Control content)
        {
            bool first = column.RowCount == 0;
            var heading = new ThemedLabel(Ramp, title, TextStyle.BodyStrong)
            {
                Margin = new Padding(0, first ? 0 : Scale.Px(Metrics.SectionGap), 0, Scale.Px(Metrics.HeadingGap)),
            };
            Ui.AddRow(column, heading);
            Ui.AddRow(column, content);
        }

        private FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
                AccessibleName = string.Empty,
            };
            foreach (Control c in controls)
            {
                c.Margin = new Padding(0, 0, Scale.Px(Metrics.ButtonGap) * 2, 0);
                row.Controls.Add(c);
            }
            return row;
        }

        // ---------------------------------------------------------------- text

        /// <summary>Each ramp style once, the three text roles, and labels with a leading glyph (16 and 20 epx).</summary>
        private ThemedTable TypeSamples()
        {
            var column = new ThemedTable(1);
            foreach (TextStyle style in new[] { TextStyle.Title, TextStyle.Subtitle, TextStyle.BodyStrong, TextStyle.Body, TextStyle.Caption, TextStyle.Mono })
                Ui.AddRow(column, new ThemedLabel(Ramp, style + ": the quick brown fox, 0.0.0.0 example.com", style));
            Ui.AddRow(column, Row(
                new ThemedLabel(Ramp, "Primary text", TextStyle.Body, TextRole.Primary),
                new ThemedLabel(Ramp, "Secondary text", TextStyle.Body, TextRole.Secondary),
                new ThemedLabel(Ramp, "Disabled text", TextStyle.Body, TextRole.Disabled)));
            Ui.AddRow(column, Row(
                new ThemedLabel(Ramp, "In use (CheckMark, AccentText)") { LeadingGlyph = Glyph.CheckMark, LeadingGlyphColor = ThemeToken.AccentText },
                new ThemedLabel(Ramp, "A fact title (Globe, 20 epx, 12 gap)", TextStyle.BodyStrong)
                {
                    LeadingGlyph = Glyph.Globe, LeadingGlyphSize = 20, LeadingGlyphGap = 12, LeadingGlyphColor = ThemeToken.TextSecondary,
                }));
            Ui.AddRow(column, new ThemedLabel(Ramp, "A details line with the caution glyph: its text wraps inside the column and the glyph stays on the first line.",
                TextStyle.Body, TextRole.Primary, 432) { LeadingGlyph = Glyph.Warning, LeadingGlyphColor = ThemeToken.CautionGlyph });
            return column;
        }

        // ---------------------------------------------------------------- buttons

        private ThemedTable ButtonGrid()
        {
            var grid = new ThemedTable(8);
            string[] columns = { "", "Rest", "Hover", "Pressed", "Disabled", "Focused", "Shield", "External" };
            var header = new Control[columns.Length];
            for (int i = 0; i < columns.Length; i++)
                header[i] = new ThemedLabel(Ramp, columns[i], TextStyle.Caption, TextRole.Secondary) { Margin = new Padding(0, 0, 0, Scale.Px(4)) };
            Ui.AddRow(grid, header);
            foreach (ButtonStyle style in new[] { ButtonStyle.Accent, ButtonStyle.Standard, ButtonStyle.Subtle })
            {
                var cells = new Control?[columns.Length];
                cells[0] = new ThemedLabel(Ramp, style.ToString(), TextStyle.Caption, TextRole.Secondary) { Anchor = AnchorStyles.Left };
                cells[1] = Button(style, ForcedState.None);
                cells[2] = Button(style, ForcedState.Hover);
                cells[3] = Button(style, ForcedState.Pressed);
                cells[4] = Button(style, ForcedState.None, enabled: false);
                cells[5] = Button(style, ForcedState.Focused);
                if (style != ButtonStyle.Subtle)
                {
                    ThemedButton shield = Button(style, ForcedState.None);
                    shield.Text = Strings.FirstRunTurnOnButton;
                    shield.Shield = true;
                    cells[6] = shield;
                    ThemedButton external = Button(style, ForcedState.None);
                    external.Text = Strings.ReportOnGitHubButton;
                    external.External = true;
                    cells[7] = external;
                }
                Ui.AddRow(grid, cells);
            }
            return grid;
        }

        private ThemedButton Button(ButtonStyle style, ForcedState state, bool enabled = true)
        {
            ThemedButton b = style == ButtonStyle.Subtle
                ? new ThemedButton(Ramp, Glyph.Close, Strings.CloseNoticeName, Metrics.NoticeClose)
                : new ThemedButton(Ramp, style == ButtonStyle.Accent ? Strings.UseStrictButton : Strings.AboutButton, style);
            b.Enabled = enabled;
            b.Margin = new Padding(0, 0, Scale.Px(Metrics.ButtonGap), Scale.Px(Metrics.ButtonGap));
            UiOverrides.Force(b, state);
            if (style != ButtonStyle.Subtle && state == ForcedState.None && enabled)
                _samples.Add((b, new Point(Scale.Px(6), Scale.Px(6)), style + " button"));
            return b;
        }

        private ThemedLink Link(string text, ForcedState state, bool enabled = true, bool external = false)
        {
            var link = new ThemedLink(Ramp, text) { Enabled = enabled, External = external };
            UiOverrides.Force(link, state);
            link.ApplyTheme(ThemeManager.Current);
            return link;
        }

        private ThemedCheckBox Check(ForcedState state, bool isChecked, bool enabled)
        {
            var box = new ThemedCheckBox(Ramp, Strings.FixShowOnlyAllowed(3)) { Checked = isChecked, Enabled = enabled };
            UiOverrides.Force(box, state);
            return box;
        }

        // ---------------------------------------------------------------- search, progress, shields

        private ThemedTable SearchBoxes()
        {
            var grid = new ThemedTable(2);
            SearchBox Box(string text, ForcedState state, bool enabled)
            {
                var box = new SearchBox(Ramp) { Width = Scale.Px(300), Hint = Strings.FixSearchHint, Enabled = enabled };
                box.TextBox.AccessibleName = Strings.FixSearchAccessibleName;
                box.Text = text;
                box.Margin = new Padding(0, 0, Scale.Px(Metrics.SectionGap), Scale.Px(Metrics.ButtonGap));
                UiOverrides.Force(box, state);
                box.ApplyTheme(ThemeManager.Current);
                return box;
            }
            Ui.AddRow(grid, Box(string.Empty, ForcedState.None, true), Box("yahoo", ForcedState.None, true));
            Ui.AddRow(grid, Box("doubleclick", ForcedState.Focused, true), Box(string.Empty, ForcedState.None, false));
            return grid;
        }

        private Control Ring(bool animate)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent, AccessibleName = string.Empty };
            var ring = new ProgressRing { AccessibleName = Strings.ChangingHostsFile, PreviewAnimations = animate };
            ring.Active = true;
            ring.Margin = new Padding(0, Scale.Px(2), Scale.Px(Metrics.ButtonGap), 0);
            row.Controls.Add(ring);
            row.Controls.Add(new ThemedLabel(Ramp, Strings.ChangingHostsFile, TextStyle.Body, TextRole.Secondary));
            return row;
        }

        private CardPanel Shields()
        {
            var card = new CardPanel(Metrics.CardPadding);
            card.Content.ColumnCount = 3;
            var cells = new Control[3];
            ShieldKind[] kinds = { ShieldKind.On, ShieldKind.Off, ShieldKind.Attention };
            for (int i = 0; i < kinds.Length; i++)
            {
                // The default size: the status card's 64 epx.
                cells[i] = new ShieldIcon
                {
                    Kind = kinds[i],
                    Margin = new Padding(0, 0, i < kinds.Length - 1 ? Scale.Px(Metrics.CardPadding) : 0, 0),
                    AccessibleName = Strings.Headline(i == 0 ? ProtectionState.On : i == 1 ? ProtectionState.Off : ProtectionState.Removed, RefusalReason.None),
                };
            }
            Ui.AddRow(card.Content, cells);
            _samples.Add((card, new Point(Scale.Px(8), Scale.Px(8)), "card"));
            return card;
        }

        private CardPanel AppIconCard(UiContext ctx)
        {
            var card = new CardPanel(Metrics.CardPadding);
            Ui.AddRow(card.Content, new AppIconImage(ctx.ExePath));
            return card;
        }

        /// <summary>A dialog's footer band at the notices' width: the left text wraps, the buttons stay right-aligned.</summary>
        private CommandBar CommandBarSample()
        {
            var bar = new CommandBar(Ramp) { FixedWidth = Scale.Px(Metrics.MainClientWidth - 2 * Metrics.PagePadding) };
            var copy = new ThemedLink(Ramp, Strings.CopyDetailsLink);
            var retry = new ThemedButton(Ramp, Strings.TryAgainButton, ButtonStyle.Accent) { Shield = true };
            var close = new ThemedButton(Ramp, Strings.CloseButton);
            bar.SetLeft(copy);
            bar.SetRight(retry, close);
            return bar;
        }

        // ---------------------------------------------------------------- notices, pills, list

        private ThemedTable Notices()
        {
            var column = new ThemedTable(1);
            int width = Scale.Px(Metrics.MainClientWidth - 2 * Metrics.PagePadding);
            void Add(NoticeBar bar)
            {
                bar.FixedWidth = width;
                bar.Margin = new Padding(0, 0, 0, Scale.Px(Metrics.NoticeGap));
                Ui.AddRow(column, bar);
                _samples.Add((bar, new Point(Scale.Px(8), Scale.Px(Metrics.NoticeMinHeight) / 2), bar.Severity + " bar"));
            }
            var error = new NoticeBar(Ramp, NoticeSeverity.Error, Strings.UnexpectedMayBeIncomplete, closable: true);
            error.Action = new ThemedButton(Ramp, Strings.TryAgainButton) { Shield = true };
            Add(error);
            var warning = new NoticeBar(Ramp, NoticeSeverity.Warning, Strings.ResultSelfCheckFailed, closable: true);
            warning.Action = new ThemedLink(Ramp, Strings.CopySelfCheckDetailsLink);
            warning.Feedback = Strings.DetailsCopied;
            Add(warning);
            var info = new NoticeBar(Ramp, NoticeSeverity.Info, Strings.TipNewerListInApp("2026-11-20"), closable: false);
            info.Action = new ThemedButton(Ramp, Strings.UseNewerListButton) { Shield = true };
            Add(info);
            var tip = new NoticeBar(Ramp, NoticeSeverity.Tip, Strings.TipRunningFromDownloads, closable: true);
            tip.Action = new ThemedLink(Ramp, Strings.HowItWorksLink);
            Add(tip);
            Add(new NoticeBar(Ramp, NoticeSeverity.Success, Strings.ResultTurnOnOk, closable: false));
            return column;
        }

        private CardPanel List(UiContext ctx)
        {
            var card = new CardPanel(4, withContent: false)
            {
                AutoSize = false,
                Size = new Size(Scale.Px(Metrics.MainClientWidth - 2 * Metrics.PagePadding), Scale.Px(250)),
            };
            var list = new ThemedListView(Ramp) { Dock = DockStyle.Fill, AccessibleName = Strings.FixListAccessibleName };
            list.Columns.Add(Strings.ColumnAddress, Scale.Px(220));
            list.Columns.Add(Strings.ColumnCompany, Scale.Px(130));
            list.Columns.Add(Strings.ColumnStatus, Scale.Px(150));
            int n = 0;
            foreach (ListEntry e in ctx.Lists.DefaultEntries.Take(6))
            {
                bool allowed = n == 2;
                var item = new ListViewItem(new[] { e.Host, e.Company, allowed ? Strings.StatusAllowed : Strings.StatusBlocked });
                list.Items.Add(item);
                n++;
            }
            list.CellPainter = cell =>
            {
                if (cell.Column != 2) return false;
                string text = cell.Item.SubItems[2].Text;
                Size size = Pill.Measure(cell.Ramp, text);
                var box = new Rectangle(cell.Content.X, cell.Content.Y + (cell.Content.Height - size.Height) / 2, size.Width, size.Height);
                Pill.Draw(cell.Graphics, cell.Ramp, text == Strings.StatusAllowed ? PillKind.Accent : PillKind.Neutral, text, box, cell.Highlighted, cell.Theme);
                return true;
            };
            list.PreviewHotIndex = 1;
            list.Items[2].Selected = true;
            list.Items[3].Selected = true;
            list.PreviewFocusRing = true;
            list.PreviewFocusedIndex = 3;
            card.Controls.Add(list);
            GalleryList = list;
            return card;
        }

        /// <summary>Tests: the gallery's list (its sample row is the third, index 2).</summary>
        internal ThemedListView? GalleryList { get; private set; }

        // ---------------------------------------------------------------- painted samples

        /// <summary>The UAC shield at 16, 20, 24 and 32 px on a card.</summary>
        private sealed class UacShields : CardPanel
        {
            private static readonly int[] Sizes = { 16, 20, 24, 32 };

            public UacShields() : base(Metrics.CardPadding, withContent: false)
            {
                Size = new Size(UiScale.Current.Px(16 * 2 + 16 + 20 + 24 + 32 + 3 * 12), UiScale.Current.Px(68));
                AccessibleName = "UAC shield";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                int x = Padding.Left;
                foreach (int px in Sizes)
                {
                    UacShield.Draw(e.Graphics, new Rectangle(x, (Height - px) / 2, px, px), disabled: false);
                    x += px + UiScale.Current.Px(12);
                }
            }
        }

        /// <summary>The focus ring as a button with a body of its own draws it: ring, inner band, then the body inside.</summary>
        private sealed class FocusSample : CardPanel
        {
            public FocusSample() : base(Metrics.CardPadding, withContent: false)
            {
                Size = new Size(UiScale.Current.Px(160), UiScale.Current.Px(68));
                AccessibleName = "Focus ring";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                UiScale s = UiScale.Current;
                Theme t = ThemeManager.Current;
                var box = new Rectangle(s.Px(16), (Height - s.Px(32)) / 2, Width - s.Px(32), s.Px(32));
                FocusRing.FillBand(e.Graphics, box, t);
                RoundRect.Prepare(e.Graphics);
                RoundRect.Fill(e.Graphics, FocusRing.Inside(box), s.Px(Metrics.Radius), t.AccentFill);
                if (t.AccentBorderWidth > 0) RoundRect.Stroke(e.Graphics, FocusRing.Inside(box), s.Px(Metrics.Radius), s.Stroke(t.AccentBorderWidth), t.ButtonBorder);
                FocusRing.DrawOuter(e.Graphics, box, t);
            }
        }

        /// <summary>Every glyph at 16, 20 and 24 epx, in TextPrimary on a card.</summary>
        private sealed class GlyphSheet : CardPanel
        {
            private static readonly int[] Sizes = { 16, 20, 24 };

            public GlyphSheet() : base(Metrics.CardPadding, withContent: false)
            {
                int count = Enum.GetValues(typeof(Glyph)).Length;
                UiScale s = UiScale.Current;
                Size = new Size(s.Px(16) * 2 + count * s.Px(40), s.Px(16) * 2 + Sizes.Length * s.Px(32));
                AccessibleName = "Glyphs";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                UiScale s = UiScale.Current;
                int y = s.Px(16);
                foreach (int size in Sizes)
                {
                    int px = s.Px(size);
                    int x = s.Px(16);
                    foreach (Glyph g in Enum.GetValues(typeof(Glyph)))
                    {
                        GlyphFont.Draw(e.Graphics, g, new Rectangle(x + (s.Px(40) - px) / 2, y + (s.Px(32) - px) / 2, px, px), ThemeManager.Current.TextPrimary);
                        x += s.Px(40);
                    }
                    y += s.Px(32);
                }
            }
        }

        /// <summary>The three pills on a normal row (the card) and on a selected row (backplate and selection bar).</summary>
        private sealed class PillSheet : CardPanel
        {
            private readonly TypeRamp _ramp;

            public PillSheet(TypeRamp ramp) : base(4, withContent: false)
            {
                _ramp = ramp;
                UiScale s = ramp.Scale;
                Size = new Size(s.Px(Metrics.MainClientWidth - 2 * Metrics.PagePadding), 2 * RowPx + s.Px(8));
                AccessibleName = "Pills";
            }

            private static readonly (PillKind Kind, string Text)[] Pills =
            {
                (PillKind.Accent, Strings.StatusAllowed),
                (PillKind.Neutral, Strings.StatusBlocked),
                (PillKind.Outline, Strings.TierName(Tier.Strict)),
            };

            /// <summary>A list row's height, as the list has it: max(Px(32), Body line + Px(12)).</summary>
            private int RowPx => _ramp.TextDriven(Metrics.ListRow, TextStyle.Body, Metrics.ButtonPadY);

            /// <summary>Tests: a point inside the first pill of a row, in its left padding (away from the text).</summary>
            internal Point PillPoint(bool selected)
            {
                UiScale s = _ramp.Scale;
                int rowTop = Padding.Top + (selected ? RowPx : 0);
                int h = Pill.Height(_ramp);
                return new Point(s.Px(16) + s.Px(4), rowTop + (RowPx - h) / 2 + h / 2);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Theme t = ThemeManager.Current;
                UiScale s = _ramp.Scale;
                Graphics g = e.Graphics;
                for (int row = 0; row < 2; row++)
                {
                    bool selected = row == 1;
                    var rowBox = new Rectangle(Padding.Left, Padding.Top + row * RowPx, Width - Padding.Horizontal, RowPx);
                    if (selected)
                    {
                        var plate = new Rectangle(rowBox.X + s.Px(4) - s.Px(4), rowBox.Y + s.Px(2), rowBox.Width, rowBox.Height - s.Px(4));
                        RoundRect.Prepare(g);
                        RoundRect.Fill(g, plate, s.Px(Metrics.Radius), t.ListSelected);
                        var bar = new Rectangle(plate.X + s.Px(4), plate.Y + (plate.Height - s.Px(16)) / 2, s.Px(3), s.Px(16));
                        RoundRect.Fill(g, bar, s.PxF(1.5f), t.SelectionBar);
                    }
                    int x = s.Px(16);
                    foreach ((PillKind kind, string text) in Pills)
                    {
                        Size size = Pill.Measure(_ramp, text);
                        var box = new Rectangle(x, rowBox.Y + (rowBox.Height - size.Height) / 2, size.Width, size.Height);
                        Pill.Draw(g, _ramp, kind, text, box, selected, t);
                        x += size.Width + s.Px(12);
                    }
                }
            }
        }
    }
}
#endif
