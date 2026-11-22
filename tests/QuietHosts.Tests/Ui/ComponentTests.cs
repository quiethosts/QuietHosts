using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// The owner-drawn components of spec 2.8: glyphs, the UAC shield, the focus ring, accessibility, sizes, the list's rows,
    /// the search box, the command row, the progress ring, pills and keyboard scrolling (T-A-16, T-A-17, T-A-20..27,
    /// T-A-29). Windows live on the test desktop; every test resets the globals it touched.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class ComponentTests
    {
        // ---------------------------------------------------------------- T-A-16

        private static int InkPixels(Glyph glyph, int px)
        {
            using (var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    GlyphFont.Draw(g, glyph, new Rectangle(0, 0, px, px), Color.Black);
                }
                int ink = 0;
                for (int y = 0; y < px; y++)
                    for (int x = 0; x < px; x++)
                        if (bmp.GetPixel(x, y).R < 200) ink++;
                return ink;
            }
        }

        [Fact]
        public void Glyphs_come_from_an_icon_font_or_as_shapes_for_the_ones_that_carry_meaning()
        {
            try
            {
                string? family = GlyphFont.Family;
                Assert.NotNull(family);
                Assert.Contains(family!, new[] { GlyphFont.FluentFamily, GlyphFont.Mdl2Family });
                var missing = Enum.GetValues(typeof(Glyph)).Cast<Glyph>().Where(g => !GlyphFont.Has(g)).ToList();
                Assert.Empty(missing);
                foreach (Glyph g in Enum.GetValues(typeof(Glyph))) Assert.True(InkPixels(g, 24) > 10, g + " draws nothing from " + family);

                // No icon font at all: the six glyphs that carry meaning are drawn as shapes, the decorative ones not at all.
                GlyphFont.PreviewNoIconFont = true;
                Assert.Null(GlyphFont.Family);
                foreach (Glyph g in Enum.GetValues(typeof(Glyph)))
                {
                    int ink = InkPixels(g, 24);
                    if (GlyphFont.CarriesMeaning(g)) Assert.True(ink > 10, g + " has no vector fallback");
                    else Assert.Equal(0, ink);
                }
                Assert.Equal(6, Enum.GetValues(typeof(Glyph)).Cast<Glyph>().Count(GlyphFont.CarriesMeaning));
            }
            finally
            {
                UiGlobals.Reset();
            }
        }

        // ---------------------------------------------------------------- T-A-17

        [Fact]
        public void The_uac_shield_is_the_shells_icon_at_exactly_the_size_asked_for()
        {
            Sta.Run(() =>
            {
                var sizes = new List<int> { UiScale.Current.Px(Metrics.ShieldIcon), 16, 20, 24, 32 };
                foreach (int px in sizes.Distinct())
                {
                    Bitmap? bmp = UacShield.Get(px);
                    Assert.NotNull(bmp);
                    Assert.Equal(px, bmp!.Width);
                    Assert.Equal(px, bmp.Height);
                    Assert.Equal(PixelFormat.Format32bppArgb, bmp.PixelFormat);
                    // The shield's point leaves the bottom corners transparent; much of the square is the opaque shield.
                    Assert.Equal(0, bmp.GetPixel(0, px - 1).A);
                    Assert.Equal(0, bmp.GetPixel(px - 1, px - 1).A);
                    int opaque = 0;
                    for (int y = 0; y < px; y++)
                        for (int x = 0; x < px; x++)
                            if (bmp.GetPixel(x, y).A == 255) opaque++;
                    Assert.True(opaque >= px * px / 4, "only " + opaque + " opaque pixels at " + px + " px");
                    // The shell's exact-size path works in the test host (no comctl32 v6 there).
                    Assert.Equal(UacShieldSource.Extracted, UacShield.SourceFor(px));
                    Assert.Same(bmp, UacShield.Get(px));
                }
            });
        }

        // ---------------------------------------------------------------- the status shield's size (a11y-6)

        [Theory]
        [InlineData(96, 64)]
        [InlineData(120, 80)]
        [InlineData(144, 96)]
        [InlineData(192, 128)]
        public void The_status_shield_is_64_epx_at_every_dpi(int dpi, int px)
        {
            Sta.Run(() =>
            {
                try
                {
                    UiScale.Override(dpi);
                    using (var shield = new ShieldIcon())
                    {
                        Assert.Equal(new Size(px, px), shield.Size);
                        Assert.Equal(Padding.Empty, shield.Margin);
                        Assert.Equal(AccessibleRole.Graphic, shield.AccessibleRole);
                    }
                    using (var icon = new AppIconImage(null))
                    {
                        Assert.Equal(new Size(UiScale.Current.Px(Metrics.AppIcon), UiScale.Current.Px(Metrics.AppIcon)), icon.Size);
                        Assert.True(icon.HasImage);
                    }
                    using (var ring = new ProgressRing())
                    {
                        Assert.Equal(new Size(UiScale.Current.Px(Metrics.ProgressRing), UiScale.Current.Px(Metrics.ProgressRing)), ring.Size);
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        // ---------------------------------------------------------------- T-A-20

        [Fact]
        public void Every_focusable_component_is_a_tab_stop_at_least_24_epx_each_way()
        {
            Sta.Run(() =>
            {
                using (var w = new TestWindow())
                {
                    TypeRamp r = w.Fonts;
                    var targets = new List<Control>
                    {
                        w.Add(new ThemedButton(r, "OK")),
                        w.Add(new ThemedButton(r, Strings.FirstRunTurnOnButton, ButtonStyle.Accent) { Shield = true }),
                        w.Add(new ThemedButton(r, Glyph.Close, Strings.CloseNoticeName, Metrics.NoticeClose)),
                        w.Add(new ThemedLink(r, Strings.ViewLicenseLink)),
                        w.Add(new ThemedLink(r, Strings.ProjectPageLink) { External = true }),
                        w.Add(new ThemedCheckBox(r, Strings.FixShowOnlyAllowed(3))),
                    };
                    SearchBox search = w.Add(new SearchBox(r) { Width = r.Scale.Px(300) });
                    search.Text = "yahoo";
                    var list = new ThemedListView(r) { AccessibleName = Strings.FixListAccessibleName, Size = new Size(r.Scale.Px(300), r.Scale.Px(120)) };
                    w.Add(list);
                    Sta.ShowOffScreen(w);
                    int min = r.Scale.Px(Metrics.TargetMin);
                    foreach (Control c in targets.Concat(new Control[] { list }))
                    {
                        Assert.True(c.TabStop, c.GetType().Name + " is no tab stop");
                        Assert.True(c.Width >= min && c.Height >= min, c.GetType().Name + " \"" + c.Text + "\" is " + c.Size);
                    }
                    // The search box: the text box is the tab stop; the whole box (at least 24 epx) is what a click focuses.
                    Assert.True(search.TextBox.TabStop);
                    Assert.True(search.Height >= min);
                    Assert.False(search.ClearButton.TabStop);
                    Assert.True(search.ClearButton.Width >= min && search.ClearButton.Height >= min);
                    MethodInfo onMouseDown = typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    targets[0].Focus();
                    onMouseDown.Invoke(search, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0) });
                    Assert.True(search.TextBox.Focused);
                }
            });
        }

        [Fact]
        public void The_ring_shows_only_with_focus_and_focus_cues()
        {
            Sta.Run(() =>
            {
                try
                {
                    using (var w = new TestWindow())
                    {
                        ThemedButton button = w.Add(new ThemedButton(w.Fonts, Strings.AboutButton));
                        ThemedButton other = w.Add(new ThemedButton(w.Fonts, Strings.CloseButton));
                        Sta.ShowOffScreen(w);
                        Assert.False(VisualState.ShowsRing(button, focused: false, showFocusCues: true));
                        Assert.False(VisualState.ShowsRing(button, focused: true, showFocusCues: false));
                        Assert.True(VisualState.ShowsRing(button, focused: true, showFocusCues: true));

                        button.Focus();
                        Assert.True(button.Focused);
                        // Without keyboard use the ring follows Windows' own setting for focus cues (off by default).
                        Assert.Equal(button.PreviewSystemFocusCues, button.PreviewShowsFocusRing);
                        using (Bitmap focused = Pixels.Of(button))
                        using (Bitmap plain = Pixels.Of(other))
                        {
                            if (!button.PreviewSystemFocusCues) Assert.Equal(Pixels.Rgb(plain, 0, plain.Height / 2), Pixels.Rgb(focused, 0, focused.Height / 2));
                        }
                        UiOverrides.ForceFocusCues = true;
                        Assert.True(button.PreviewShowsFocusRing);
                        Assert.False(other.PreviewShowsFocusRing);
                        using (Bitmap focused = Pixels.Of(button))
                            Assert.Equal(Pixels.Rgb(ThemeManager.Current.FocusOuter), Pixels.Rgb(focused, 0, focused.Height / 2));
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        /// <summary>
        /// The ring is visible on every edge: the outer ring, the inner ring, then (for a control with a body of its own) a
        /// body edge that isn't the inner ring's color, and the edge looks different from the same control without focus.
        /// Before the body moved inside the ring, an accent button in high contrast failed here (its border hid the ring).
        /// </summary>
        private static void CheckRingVisible(Bitmap focused, Bitmap plain, Theme t, bool body, bool topAndBottom, string what, List<string> problems)
        {
            UiScale s = UiScale.Current;
            int outer = s.Stroke(Metrics.FocusOuter), inner = s.Stroke(Metrics.FocusInner);
            int w = focused.Width, h = focused.Height;
            var edges = new List<(string Edge, Func<int, Point> At)>
            {
                ("left", i => new Point(i, h / 2)),
                ("right", i => new Point(w - 1 - i, h / 2)),
            };
            if (topAndBottom)
            {
                edges.Add(("top", i => new Point(w / 2, i)));
                edges.Add(("bottom", i => new Point(w / 2, h - 1 - i)));
            }
            foreach ((string edge, Func<int, Point> at) in edges)
            {
                string where = t.Name + ", " + what + ", " + edge + " edge";
                for (int i = 0; i < outer; i++)
                {
                    Point p = at(i);
                    if (Pixels.Rgb(focused, p.X, p.Y) != Pixels.Rgb(t.FocusOuter)) problems.Add(where + ": no outer ring at " + i);
                }
                Point innerAt = at(outer);
                if (Pixels.Rgb(focused, innerAt.X, innerAt.Y) != Pixels.Rgb(t.FocusInner)) problems.Add(where + ": no inner ring");
                if (body)
                {
                    Point bodyAt = at(outer + inner);
                    if (Pixels.Rgb(focused, bodyAt.X, bodyAt.Y) == Pixels.Rgb(t.FocusInner)) problems.Add(where + ": the body's edge melts into the inner ring");
                }
                bool differs = false;
                for (int i = 0; i < outer + inner + 2; i++)
                {
                    Point p = at(i);
                    if (Pixels.Rgb(focused, p.X, p.Y) != Pixels.Rgb(plain, p.X, p.Y)) differs = true;
                }
                if (!differs) problems.Add(where + ": the ring changes nothing");
            }
        }

        [Fact]
        public void The_focus_ring_is_visible_on_every_component_in_every_theme()
        {
            Sta.Run(() =>
            {
                var problems = new List<string>();
                try
                {
                    foreach (string name in new[] { "light", "dark", "hc-black", "hc-white" })
                    {
                        ThemeManager.Override(Theme.FromName(name)!);
                        Theme t = ThemeManager.Current;
                        using (var w = new TestWindow())
                        {
                            TypeRamp r = w.Fonts;
                            var cases = new List<(Control Focused, Control Plain, bool Body, bool TopAndBottom, string What)>();
                            void Pair(Func<Control> make, bool body, bool topAndBottom, string what)
                            {
                                Control a = w.Add(make()), b = w.Add(make());
                                UiOverrides.Force(a, ForcedState.Focused);
                                cases.Add((a, b, body, topAndBottom, what));
                            }
                            Pair(() => new ThemedButton(r, Strings.UseStrictButton, ButtonStyle.Accent), true, true, "accent button");
                            Pair(() => new ThemedButton(r, Strings.FirstRunTurnOnButton, ButtonStyle.Accent) { Shield = true }, true, true, "accent button with the shield");
                            Pair(() => new ThemedButton(r, Strings.AboutButton), true, true, "standard button");
                            Pair(() => new ThemedButton(r, Glyph.Close, Strings.CloseNoticeName, Metrics.NoticeClose), false, true, "subtle button");
                            Pair(() => new ThemedCheckBox(r, Strings.FixShowOnlyAllowed(3)) { Checked = true }, false, true, "check box");
                            Pair(() => new ThemedLink(r, Strings.ViewLicenseLink), false, false, "link");
                            Sta.ShowOffScreen(w);
                            foreach ((Control focused, Control plain, bool body, bool topAndBottom, string what) in cases)
                            {
                                using (Bitmap a = Pixels.Of(focused))
                                using (Bitmap b = Pixels.Of(plain))
                                {
                                    CheckRingVisible(a, b, t, body, topAndBottom, what, problems);
                                }
                            }
                            // The check box keeps its box clear of the ring.
                            var box = (ThemedCheckBox)cases.Single(c => c.What == "check box").Focused;
                            Assert.True(box.BoxBounds.X >= FocusRing.Width(r.Scale));
                        }
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
                Assert.Empty(problems);
            });
        }

        // ---------------------------------------------------------------- T-A-21

        [Fact]
        public void Components_tell_screen_readers_their_role_name_and_description()
        {
            Sta.Run(() =>
            {
                using (var w = new TestWindow())
                {
                    TypeRamp r = w.Fonts;
                    ThemedButton main = w.Add(new ThemedButton(r, Strings.FirstRunTurnOnButton, ButtonStyle.Accent) { Shield = true, ContextDescription = "Protection is off." });
                    ThemedButton report = w.Add(new ThemedButton(r, Strings.ReportOnGitHubButton) { External = true });
                    ThemedButton both = w.Add(new ThemedButton(r, Strings.AboutButton) { Shield = true, External = true, ContextDescription = "Context." });
                    ThemedButton close = w.Add(new ThemedButton(r, Glyph.Close, Strings.CloseNoticeName));
                    ThemedLink link = w.Add(new ThemedLink(r, Strings.ProjectPageLink) { External = true });
                    ThemedLink plainLink = w.Add(new ThemedLink(r, Strings.ViewLicenseLink) { ContextDescription = "The MIT text." });
                    ThemedCheckBox check = w.Add(new ThemedCheckBox(r, Strings.FixShowOnlyAllowed(3)) { Checked = true });
                    var warning = new NoticeBar(r, NoticeSeverity.Warning, "Some lines changed.", closable: true);
                    var tip = new NoticeBar(r, NoticeSeverity.Tip, "Tip: keep it in a folder of its own.", closable: true);
                    var plainTip = new NoticeBar(r, NoticeSeverity.Tip, "Keep it in a folder of its own.", closable: true);
                    var note = new NoticeBar(r, NoticeSeverity.Info, "A newer list.", closable: false);
                    var done = new NoticeBar(r, NoticeSeverity.Success, "Protection is on.", closable: true);
                    var error = new NoticeBar(r, NoticeSeverity.Error, "It failed.", closable: false);
                    w.Add(warning);
                    w.Add(tip);
                    w.Add(plainTip);
                    w.Add(note);
                    w.Add(done);
                    w.Add(error);
                    var ring = w.Add(new ProgressRing { AccessibleName = Strings.ChangingHostsFile });
                    var shield = w.Add(new ShieldIcon { AccessibleName = Strings.Headline(QuietHosts.Core.ProtectionState.On, QuietHosts.Core.RefusalReason.None) });
                    var glyph = w.Add(new GlyphIcon(Glyph.Error, 32, ThemeToken.CriticalGlyph, "Error"));
                    var icon = w.Add(new AppIconImage(null));
                    var named = w.Add(new CardPanel { AccessibleName = "Strict" });
                    var unnamed = w.Add(new CardPanel());
                    SearchBox search = w.Add(new SearchBox(r));
                    search.TextBox.AccessibleName = Strings.FixSearchAccessibleName;
                    search.TextBox.AccessibleDescription = "Steps. Site, company or address";
                    ControlTree.CreateHandles(w);

                    Assert.Equal(AccessibleRole.PushButton, main.AccessibilityObject.Role);
                    Assert.Equal("Turn on protection", main.AccessibilityObject.Name);
                    Assert.Equal("Protection is off. " + Strings.ShieldDescription, main.AccessibleDescription);
                    Assert.Equal(Strings.OpensInBrowser, report.AccessibleDescription);
                    Assert.Equal("Context. " + Strings.ShieldDescription + " " + Strings.OpensInBrowser, both.AccessibleDescription);
                    Assert.Equal(AccessibleRole.PushButton, close.AccessibilityObject.Role);
                    Assert.Equal(Strings.CloseNoticeName, close.AccessibilityObject.Name);
                    // A button never takes another role: a Button with the Link role is a pattern-less pane in UI Automation.
                    foreach (ThemedButton b in ControlTree.All(w).OfType<ThemedButton>())
                    {
                        Assert.Equal(AccessibleRole.Default, b.AccessibleRole);
                        Assert.Equal(AccessibleRole.PushButton, b.AccessibilityObject.Role);
                    }

                    Assert.IsAssignableFrom<LinkLabel>(link);
                    Assert.Equal("Project page", link.AccessibleName);
                    Assert.Equal(Strings.OpensInBrowser, link.AccessibleDescription);
                    Assert.Equal("The MIT text.", plainLink.AccessibleDescription);
                    Assert.True(link.PreviewTakesEnter);

                    Assert.Equal(AccessibleRole.CheckButton, check.AccessibilityObject.Role);
                    Assert.True((check.AccessibilityObject.State & AccessibleStates.Checked) != 0);
                    Assert.Equal("Show only addresses you allowed (3)", check.AccessibilityObject.Name);

                    Assert.Equal(AccessibleRole.Grouping, warning.AccessibilityObject.Role);
                    Assert.Equal("Warning: Some lines changed.", warning.AccessibleName);
                    // "Tip: " is not doubled when the message already starts with it.
                    Assert.Equal("Tip: keep it in a folder of its own.", tip.AccessibleName);
                    Assert.Equal("Tip: Keep it in a folder of its own.", plainTip.AccessibleName);
                    Assert.Equal("Note: A newer list.", note.AccessibleName);
                    Assert.Equal("Done: Protection is on.", done.AccessibleName);
                    Assert.Equal("Error: It failed.", error.AccessibleName);
                    Assert.Equal("Error", error.SeverityGlyph.AccessibleName);
                    Assert.Equal(Glyph.Error, error.SeverityGlyph.Glyph);
                    Assert.Equal(Glyph.Success, done.SeverityGlyph.Glyph);
                    Assert.Equal(Glyph.Info, tip.SeverityGlyph.Glyph);
                    Assert.Equal(Glyph.Warning, warning.SeverityGlyph.Glyph);
                    Assert.Null(note.CloseButton);
                    Assert.Equal(Strings.CloseNoticeName, warning.CloseButton!.AccessibleName);
                    Assert.Equal(warning.AccessibleName, warning.CloseButton.AccessibleDescription);
                    Assert.Equal(AccessibleRole.Graphic, warning.SeverityGlyph.AccessibilityObject.Role);
                    Assert.Equal("Warning", warning.SeverityGlyph.AccessibleName);

                    Assert.Equal(AccessibleRole.ProgressBar, ring.AccessibilityObject.Role);
                    Assert.Equal(Strings.ChangingHostsFile, ring.AccessibilityObject.Name);
                    foreach (Control graphic in new Control[] { shield, glyph, icon })
                    {
                        Assert.Equal(AccessibleRole.Graphic, graphic.AccessibilityObject.Role);
                        Assert.False(string.IsNullOrEmpty(graphic.AccessibilityObject.Name));
                        Assert.False(graphic.TabStop);
                    }
                    Assert.Equal(AccessibleRole.Grouping, named.AccessibilityObject.Role);
                    Assert.Equal(AccessibleRole.Pane, unnamed.AccessibilityObject.Role);

                    Assert.Equal(AccessibleRole.Pane, search.AccessibilityObject.Role);
                    Assert.Equal(Strings.FixSearchAccessibleName, search.TextBox.AccessibilityObject.Name);
                    Assert.Equal("Steps. Site, company or address", search.TextBox.AccessibleDescription);
                    Assert.Equal(Strings.ClearSearchName, search.ClearButton.AccessibleName);
                }
            });
        }

        [Fact]
        public void Notice_bars_follow_the_severity_table_and_the_layout_metrics()
        {
            Assert.Equal(ThemeToken.CriticalBar, NoticeBar.FillFor(NoticeSeverity.Error));
            Assert.Equal(ThemeToken.CautionBar, NoticeBar.FillFor(NoticeSeverity.Warning));
            Assert.Equal(ThemeToken.CardBackground, NoticeBar.FillFor(NoticeSeverity.Info));
            Assert.Equal(ThemeToken.CardBackground, NoticeBar.FillFor(NoticeSeverity.Tip));
            Assert.Equal(ThemeToken.SuccessBar, NoticeBar.FillFor(NoticeSeverity.Success));
            Assert.Equal(ThemeToken.CriticalGlyph, NoticeBar.GlyphColorFor(NoticeSeverity.Error));
            Assert.Equal(ThemeToken.CautionGlyph, NoticeBar.GlyphColorFor(NoticeSeverity.Warning));
            Assert.Equal(ThemeToken.InfoGlyph, NoticeBar.GlyphColorFor(NoticeSeverity.Tip));
            Assert.Equal(ThemeToken.SuccessGlyph, NoticeBar.GlyphColorFor(NoticeSeverity.Success));
            Assert.Equal("Warning", Strings.NoticeGlyphName(NoticeSeverity.Warning));
            Assert.Equal("Done", Strings.NoticeGlyphName(NoticeSeverity.Success));

            Sta.Run(() =>
            {
                using (var w = new TestWindow())
                {
                    UiScale s = w.Fonts.Scale;
                    int width = s.Px(528);
                    var one = new NoticeBar(w.Fonts, NoticeSeverity.Success, "Short.", closable: true) { FixedWidth = width };
                    var full = new NoticeBar(w.Fonts, NoticeSeverity.Warning, "The list is in your hosts file, but QuietHosts couldn't confirm that Windows uses it yet.", closable: false) { FixedWidth = width };
                    var copy = new ThemedLink(w.Fonts, Strings.CopySelfCheckDetailsLink);
                    full.Action = copy;
                    full.Feedback = Strings.DetailsCopied;
                    w.Add(one);
                    w.Add(full);
                    Sta.ShowOffScreen(w);

                    // A one-line bar is the minimum 48 epx tall, its content centered; the close button 8 epx from the top and right.
                    Assert.Equal(s.Px(Metrics.NoticeMinHeight), one.Height);
                    Assert.Equal(new Rectangle(width - s.Px(8) - s.Px(32), s.Px(8), s.Px(32), s.Px(32)), one.CloseButton!.Bounds);
                    Assert.Equal(s.Px(16), one.SeverityGlyph.Left);
                    Assert.Equal(new Size(s.Px(20), s.Px(20)), one.SeverityGlyph.Size);
                    Assert.Equal(s.Px(16) + s.Px(20) + s.Px(12), one.MessageLabel.Left);

                    // Message, 8 epx, the action (a link's text lined up with the message), 4 epx, the feedback; 12 epx padding.
                    Assert.Equal(s.Px(12), full.MessageLabel.Top);
                    Assert.Equal(full.MessageLabel.Bottom + s.Px(8), copy.Top);
                    Assert.Equal(full.MessageLabel.Left, copy.Left + copy.Padding.Left);
                    Label feedback = ControlTree.All(full).OfType<ThemedLabel>().Single(l => l.Text == Strings.DetailsCopied);
                    Assert.Equal(copy.Bottom + s.Px(4), feedback.Top);
                    Assert.Equal(full.Height - s.Px(12), feedback.Bottom);
                    // Not closable: 16 epx on the right, so the message may use the room of the close button.
                    Assert.Null(full.CloseButton);
                    Assert.True(full.MessageLabel.Right <= width - s.Px(16));
                    // The action comes before the close button in the tab order.
                    Assert.Equal(0, copy.TabIndex);
                    Assert.Equal(1, one.CloseButton.TabIndex);
                }
            });
        }

        [Fact]
        public void A_disabled_link_is_drawn_in_the_disabled_text_color_without_an_underline()
        {
            Sta.Run(() =>
            {
                try
                {
                    ThemeManager.Override(Theme.Dark);
                    using (var w = new TestWindow())
                    {
                        ThemedLink link = w.Add(new ThemedLink(w.Fonts, Strings.OpenBackupFolderLink) { Enabled = false });
                        Sta.ShowOffScreen(w);
                        Assert.Equal(LinkBehavior.NeverUnderline, link.LinkBehavior);
                        using (Bitmap bmp = Pixels.Of(link))
                        {
                            // On the dark page every text pixel lies between the page and TextDisabled (#7E7E7E). LinkLabel's own
                            // disabled painting (an emboss in system colors such as #A0A0A0 and white) is lighter than that.
                            int brightest = 0;
                            for (int y = 0; y < bmp.Height; y++)
                            {
                                for (int x = 0; x < bmp.Width; x++)
                                {
                                    Color c = bmp.GetPixel(x, y);
                                    brightest = Math.Max(brightest, Math.Max(c.R, Math.Max(c.G, c.B)));
                                }
                            }
                            Assert.InRange(brightest, 0x50, 0x80);
                        }
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        private static readonly MethodInfo GetStyle = typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;

        [Fact]
        public void Every_window_names_its_focusable_controls_and_has_no_role_none()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var problems = new List<string>();
                    foreach (UiScenario s in UiScenarios.All(p.Context))
                    {
                        using (Form f = s.Create())
                        {
                            ControlTree.CreateHandles(f);
                            s.AfterShown?.Invoke(f);
                            foreach (Control c in ControlTree.All(f))
                            {
                                string what = s.Name + ": " + c.GetType().Name + " \"" + c.Text + "\"";
                                if (c.AccessibleRole == AccessibleRole.None) problems.Add(what + " has the role None");
                                bool focusable = c.TabStop && (bool)GetStyle.Invoke(c, new object[] { ControlStyles.Selectable })!;
                                if (!focusable || !ControlTree.IsMeantToShow(c, f)) continue;
                                // A text box's or list's text is its content, not a name: they need an explicit one.
                                bool needsName = c is TextBoxBase || c is ListView;
                                string? name = needsName ? c.AccessibleName : (string.IsNullOrEmpty(c.AccessibleName) ? c.Text : c.AccessibleName);
                                if (string.IsNullOrWhiteSpace(name)) problems.Add(what + " has no name");
                            }
                        }
                    }
                    Assert.Empty(problems);
                });
            }
        }

        // ---------------------------------------------------------------- T-A-22

        [Fact]
        public void Buttons_are_at_least_120_by_32_epx_and_grow_with_their_content_and_the_text_size()
        {
            Sta.Run(() =>
            {
                using (var normal = new TypeRamp(9f))
                using (var large = new TypeRamp(13.5f))
                {
                    UiScale s = UiScale.Current;
                    Size ok = new ThemedButton(normal, "OK").GetPreferredSize(Size.Empty);
                    Assert.Equal(s.Px(Metrics.ButtonMinWidth), ok.Width);
                    Assert.Equal(normal.TextDriven(Metrics.ButtonHeight, TextStyle.Body, Metrics.ButtonPadY), ok.Height);
                    Assert.True(ok.Height >= s.Px(Metrics.ButtonHeight));

                    const string longText = "Check for a newer version now";
                    // The label's width as it is drawn (NoPadding), measured on a device context: measured without one, GDI's
                    // blank margins would count (9 px for Body at 96 DPI) and put the label off centre.
                    int textWidth;
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        textWidth = TextRenderer.MeasureText(g, longText, normal.Body, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    Assert.True(TextRenderer.MeasureText(longText, normal.Body, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > textWidth,
                        "this .NET measures NoPadding without a device context now; the comment above is out of date");
                    Size wide = new ThemedButton(normal, longText).GetPreferredSize(Size.Empty);
                    Assert.Equal(s.Px(12) + textWidth + s.Px(12), wide.Width);
                    Assert.True(wide.Width > s.Px(Metrics.ButtonMinWidth));
                    Size shielded = new ThemedButton(normal, longText) { Shield = true }.GetPreferredSize(Size.Empty);
                    Assert.Equal(wide.Width + s.Px(16) + s.Px(8), shielded.Width);
                    Assert.Equal(wide.Width + s.Px(24), shielded.Width);
                    Size external = new ThemedButton(normal, longText) { External = true }.GetPreferredSize(Size.Empty);
                    Assert.Equal(wide.Width + s.Px(4) + s.Px(16), external.Width);
                    // The access key's "&" takes no room.
                    Assert.Equal(wide.Width, new ThemedButton(normal, "&" + longText).GetPreferredSize(Size.Empty).Width);

                    Size bigger = new ThemedButton(large, "OK").GetPreferredSize(Size.Empty);
                    Assert.True(bigger.Height > ok.Height);
                    Assert.Equal(Math.Max(s.Px(32), large.LineHeight(TextStyle.Body) + s.Px(12)), bigger.Height);

                    Assert.Equal(new Size(s.Px(32), s.Px(32)), new ThemedButton(normal, Glyph.Close, Strings.CloseNoticeName, Metrics.NoticeClose).GetPreferredSize(Size.Empty));
                    Assert.Equal(new Size(s.Px(24), s.Px(24)), new ThemedButton(normal, Glyph.Close, Strings.ClearSearchName, Metrics.ClearButton).GetPreferredSize(Size.Empty));
                    Assert.Equal(new Size(s.Px(24), s.Px(24)), new ThemedButton(normal, Glyph.Close, Strings.ClearSearchName, 16).GetPreferredSize(Size.Empty));
                }
            });
        }

        [Fact]
        public void A_buttons_label_sits_in_its_middle()
        {
            // A label measured with GDI's blank margins but drawn without them sat 4 to 7 px left of the middle (visual-4).
            Sta.Run(() =>
            {
                try
                {
                    ThemeManager.Override(Theme.Light);
                    using (var w = new TestWindow())
                    {
                        var plain = w.Add(new ThemedButton(w.Fonts, Strings.CloseButton));
                        var shielded = w.Add(new ThemedButton(w.Fonts, Strings.MainButtonTurnOff) { Shield = true });
                        var wide = w.Add(new ThemedButton(w.Fonts, Strings.CopyButton) { Width = w.Fonts.Scale.Px(200), AutoSize = false });
                        Sta.ShowOffScreen(w);
                        foreach (ThemedButton b in new[] { plain, shielded, wide })
                        {
                            (int left, int right) = InkColumns(b);
                            Assert.True(left > 0 && right > left, b.Text + ": no label drawn");
                            int before = left, after = b.Width - 1 - right;
                            Assert.True(Math.Abs(before - after) <= 2, b.Text + ": " + before + " px before the label and " + after + " px after it");
                        }
                        // Its width follows 2.8.3: 12 + [16 + 8] + the text as drawn + 12, at least 120 epx.
                        int text;
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                            text = TextRenderer.MeasureText(g, Strings.MainButtonTurnOff, w.Fonts.Body, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                        UiScale s = w.Fonts.Scale;
                        Assert.Equal(Math.Max(s.Px(120), s.Px(12) + s.Px(16) + s.Px(8) + text + s.Px(12)), shielded.Width);
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        /// <summary>The first and last columns with ink (the label, the shield) in the middle half of a button, away from its border.</summary>
        private static (int Left, int Right) InkColumns(ThemedButton b)
        {
            using (Bitmap bmp = Pixels.Of(b))
            {
                int fill = Pixels.Rgb(bmp, b.Width / 2, 3);
                Color f = Color.FromArgb(fill);
                int left = -1, right = -1;
                for (int x = 3; x < b.Width - 3; x++)
                {
                    for (int y = b.Height / 4; y < b.Height * 3 / 4; y++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (Math.Abs(c.R - f.R) + Math.Abs(c.G - f.G) + Math.Abs(c.B - f.B) < 90) continue;
                        if (left < 0) left = x;
                        right = x;
                        break;
                    }
                }
                return (left, right);
            }
        }

        [Fact]
        public void Enter_is_swallowed_only_by_buttons_that_ask_for_it()
        {
            Sta.Run(() =>
            {
                using (var w = new TestWindow())
                {
                    int clicks = 0;
                    ThemedButton guarded = w.Add(new ThemedButton(w.Fonts, Strings.FirstRunTurnOnButton, ButtonStyle.Accent) { SwallowsEnter = true });
                    guarded.Click += (s, e) => clicks++;
                    Sta.ShowOffScreen(w);
                    MethodInfo process = typeof(Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Assert.True((bool)process.Invoke(guarded, new object[] { Keys.Enter })!);
                    Assert.True((bool)process.Invoke(guarded, new object[] { Keys.Enter | Keys.Shift })!);
                    w.ActiveControl = guarded;
                    Assert.True((bool)process.Invoke(w, new object[] { Keys.Enter })!);
                    Assert.Equal(0, clicks);
                }
            });
        }

        // ---------------------------------------------------------------- T-A-23

        [Theory]
        [InlineData(9f)]
        [InlineData(13.5f)]
        public void List_rows_have_the_spec_height_with_or_without_the_image_list(float basePoints)
        {
            Sta.Run(() =>
            {
                using (var ramp = new TypeRamp(basePoints))
                using (var w = new TestWindow())
                {
                    ThemedListView MakeList(bool imageList)
                    {
                        var list = new ThemedListView(ramp) { AccessibleName = Strings.FixListAccessibleName, Size = new Size(ramp.Scale.Px(400), ramp.Scale.Px(200)) };
                        if (!imageList) list.PreviewWithoutImageList();
                        list.Columns.Add(Strings.ColumnAddress, ramp.Scale.Px(200));
                        list.Columns.Add(Strings.ColumnStatus, ramp.Scale.Px(150));
                        for (int i = 0; i < 5; i++) list.Items.Add(new ListViewItem(new[] { "a" + i + ".ads-one.net", Strings.StatusBlocked }));
                        return w.Add(list);
                    }
                    ThemedListView normal = MakeList(imageList: true);
                    ThemedListView fallback = MakeList(imageList: false);
                    Sta.ShowOffScreen(w);
                    Assert.Equal(Math.Min(256, ramp.TextDriven(Metrics.ListRow, TextStyle.Body, Metrics.ButtonPadY)), normal.RowHeight);
                    Assert.InRange(normal.GetItemRect(0).Height, normal.RowHeight - 1, normal.RowHeight + 1);
                    normal.EnsureRowHeight();
                    Assert.False(normal.UsesFontFallback);

                    fallback.EnsureRowHeight();
                    Assert.True(fallback.UsesFontFallback);
                    Assert.InRange(fallback.GetItemRect(0).Height, fallback.RowHeight - 1, fallback.RowHeight + 1);
                }
            });
        }

        [Fact]
        public void The_list_paints_its_rows_itself_in_the_theme_colors()
        {
            Sta.Run(() =>
            {
                try
                {
                    foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
                    {
                        ThemeManager.Override(theme);
                        using (var w = new TestWindow())
                        {
                            var list = new ThemedListView(w.Fonts) { AccessibleName = Strings.FixListAccessibleName, Size = new Size(w.Fonts.Scale.Px(400), w.Fonts.Scale.Px(300)) };
                            list.Columns.Add(Strings.ColumnAddress, w.Fonts.Scale.Px(250));
                            list.Columns.Add(Strings.ColumnStatus, w.Fonts.Scale.Px(150));
                            for (int i = 0; i < 4; i++) list.Items.Add(new ListViewItem(new[] { "a" + i + ".ads-one.net", Strings.StatusBlocked }));
                            w.Add(list);
                            Sta.ShowOffScreen(w);
                            list.Items[1].Selected = true;
                            list.PreviewHotIndex = 2;
                            UiScale s = w.Fonts.Scale;
                            using (Bitmap bmp = Pixels.Of(list))
                            {
                                Rectangle selected = list.Items[1].Bounds, hot = list.Items[2].Bounds, plain = list.Items[0].Bounds;
                                // Inside the backplate, right of the selection bar and above the text's line: the plate's own color,
                                // not darkened by the native list's selection (DarkMode_Explorer paints one when it is allowed to).
                                Assert.Equal(Pixels.Rgb(theme.ListSelected), Pixels.Rgb(bmp, selected.X + s.Px(13), selected.Y + s.Px(4)));
                                Assert.Equal(Pixels.Rgb(theme.SelectionBar), Pixels.Rgb(bmp, selected.X + s.Px(9), selected.Y + selected.Height / 2));
                                Assert.Equal(Pixels.Rgb(theme.CardBackground), Pixels.Rgb(bmp, selected.X + s.Px(1), selected.Y + selected.Height / 2));
                                Assert.Equal(Pixels.Rgb(theme.ListHover), Pixels.Rgb(bmp, hot.X + s.Px(13), hot.Y + s.Px(4)));
                                Assert.Equal(Pixels.Rgb(theme.CardBackground), Pixels.Rgb(bmp, plain.X + s.Px(13), plain.Y + s.Px(4)));
                                // Below the last row: the card color only (no column lines).
                                int below = list.Items[3].Bounds.Bottom + s.Px(10);
                                for (int x = 0; x < list.ClientSize.Width; x++)
                                    Assert.Equal(Pixels.Rgb(theme.CardBackground), Pixels.Rgb(bmp, x, below));
                            }
                        }
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        // ---------------------------------------------------------------- T-A-24

        [Fact]
        public void The_search_box_clears_at_once_and_paints_its_hint_only_when_empty()
        {
            Sta.Run(() =>
            {
                using (var w = new TestWindow())
                {
                    SearchBox search = w.Add(new SearchBox(w.Fonts) { Width = w.Fonts.Scale.Px(400), Hint = "Site, company or address, for example yahoo" });
                    search.TextBox.AccessibleName = Strings.FixSearchAccessibleName;
                    ThemedButton first = w.Add(new ThemedButton(w.Fonts, "OK"));
                    int filters = 0;
                    search.FilterRequested += (s, e) => filters++;
                    Sta.ShowOffScreen(w);

                    Assert.False(search.ClearButton.Visible);
                    Assert.True(search.TextBox.ShowsHint);
                    int hintInk = InkRightOfStart(search, w);
                    Assert.True(hintInk > 20, "no hint painted (" + hintInk + ")");

                    search.Text = "a";
                    Assert.True(search.ClearButton.Visible);
                    Assert.False(search.ClearButton.TabStop);
                    Assert.False(search.TextBox.ShowsHint);
                    Assert.Equal(0, InkRightOfStart(search, w));

                    first.Focus();
                    search.ClearButton.PerformClick();
                    Assert.Equal(string.Empty, search.Text);
                    Assert.Equal(1, filters);
                    Assert.True(search.TextBox.Focused);
                    Assert.False(search.ClearButton.Visible);
                    Assert.True(search.TextBox.ShowsHint);
                }
            });
        }

        /// <summary>Pixels unlike the box's fill in the right two thirds of the text area (where a one-letter text never reaches).</summary>
        private static int InkRightOfStart(SearchBox search, TestWindow w)
        {
            using (Bitmap bmp = Pixels.Of(search))
            {
                Rectangle text = search.TextBox.Bounds;
                int fill = Pixels.Rgb(bmp, text.X + text.Width - 2, search.Height / 2);
                int ink = 0;
                for (int x = text.X + text.Width / 3; x < text.Right - 2; x++)
                    for (int y = text.Y; y < text.Bottom; y++)
                        if (Pixels.Rgb(bmp, x, y) != fill) ink++;
                return ink;
            }
        }

        // ---------------------------------------------------------------- T-A-25

        [Fact]
        public void A_command_row_keeps_one_row_when_it_fits_and_stacks_and_wraps_when_not()
        {
            Sta.Run(() =>
            {
                using (var r = new TypeRamp(9f))
                using (var row = new CommandRow(r))
                {
                    UiScale s = r.Scale;
                    var label = new ThemedLabel(r, "Showing 1,035 of 1,035");
                    var a = new ThemedButton(r, Strings.AllowButton, ButtonStyle.Accent);
                    var b = new ThemedButton(r, Strings.BlockAgainButton);
                    var c = new ThemedButton(r, Strings.CloseButton);
                    row.SetLeft(label);
                    row.SetRight(a, b, c);
                    int buttons = a.GetPreferredSize(Size.Empty).Width + b.GetPreferredSize(Size.Empty).Width + c.GetPreferredSize(Size.Empty).Width + 2 * s.Px(8);
                    int labelWidth = label.GetPreferredSize(Size.Empty).Width;

                    void LayOut(int width)
                    {
                        row.FixedWidth = width;
                        row.Size = row.GetPreferredSize(Size.Empty);
                        row.PerformLayout();
                    }

                    // Room for everything: one row, the buttons 8 epx apart at the right edge, in order.
                    LayOut(labelWidth + s.Px(16) + buttons + s.Px(40));
                    Assert.False(row.IsStacked);
                    Assert.Equal(1, row.RightRows);
                    Assert.Equal(0, label.Left);
                    Assert.Equal(row.Width, c.Right);
                    Assert.Equal(b.Right + s.Px(8), c.Left);
                    Assert.Equal(a.Right + s.Px(8), b.Left);
                    Assert.Equal(a.Top, c.Top);

                    // Not enough for both: the left group on its own row, the buttons below it, still right-aligned.
                    LayOut(buttons + s.Px(10));
                    Assert.True(row.IsStacked);
                    Assert.Equal(1, row.RightRows);
                    Assert.True(label.Bottom <= a.Top);
                    Assert.Equal(row.Width, c.Right);

                    // Too narrow for three buttons: two rows, each right-aligned, in order.
                    LayOut(a.Width + b.Width + s.Px(8) + s.Px(10));
                    Assert.True(row.IsStacked);
                    Assert.Equal(2, row.RightRows);
                    Assert.Equal(a.Top, b.Top);
                    Assert.True(c.Top > b.Top);
                    Assert.Equal(row.Width, b.Right);
                    Assert.Equal(row.Width, c.Right);
                    Assert.True(a.Left < b.Left);

                    // Tab order: the left group, then the buttons in visual order; screen readers read the children in that
                    // order too (the right group was set first here).
                    Assert.True(label.TabIndex < a.TabIndex && a.TabIndex < b.TabIndex && b.TabIndex < c.TabIndex);
                    var reorder = new CommandRow(r);
                    var later = new ThemedLabel(r, "Showing 3 of 3");
                    var first = new ThemedButton(r, Strings.CloseButton);
                    reorder.SetRight(first);
                    reorder.SetLeft(later);
                    Assert.Equal(new Control[] { later, first }, reorder.Controls.Cast<Control>().ToArray());
                    Assert.Equal(string.Empty, reorder.AccessibilityObject.Name);
                    reorder.Dispose();

                    // The dialog footer band: padding 16 epx top and bottom, 24 left and right, the divider on top.
                    var bar = new CommandBar(r);
                    Assert.Equal(new Padding(s.Px(24), s.Px(16), s.Px(24), s.Px(16)), bar.Padding);
                    Assert.True(bar.ShowDivider);
                    bar.Dispose();
                }
            });
        }

        [Fact]
        public void A_right_group_alone_wraps_into_right_aligned_rows_when_it_is_wider_than_the_row()
        {
            // The Undo dialog at 225% text: Remove everything and Cancel are wider together than the 432 epx row; they used to
            // stay on one row that started left of the row's edge, cutting off Remove everything and its shield (a11y-4).
            Sta.Run(() =>
            {
                using (var r = new TypeRamp(9f))
                using (var row = new CommandRow(r))
                {
                    UiScale s = r.Scale;
                    var remove = new ThemedButton(r, Strings.RemoveEverythingButton) { Shield = true };
                    var cancel = new ThemedButton(r, Strings.CancelButton, ButtonStyle.Accent);
                    row.SetRight(remove, cancel);
                    int removeWidth = remove.GetPreferredSize(Size.Empty).Width, cancelWidth = cancel.GetPreferredSize(Size.Empty).Width;
                    row.FixedWidth = removeWidth + s.Px(8) + cancelWidth - s.Px(10);
                    row.Size = row.GetPreferredSize(Size.Empty);
                    row.PerformLayout();
                    Assert.True(row.IsStacked);
                    Assert.Equal(2, row.RightRows);
                    // No empty left row above them: the first row starts at the top, every button inside the row, right-aligned.
                    Assert.Equal(0, remove.Top);
                    Assert.True(remove.Left >= 0 && cancel.Left >= 0, "a button starts left of the row");
                    Assert.Equal(row.Width, remove.Right);
                    Assert.Equal(row.Width, cancel.Right);
                    Assert.Equal(remove.Bottom + s.Px(8), cancel.Top);
                    Assert.Equal(row.Height, cancel.Bottom);
                    // Wide enough again: one row.
                    row.FixedWidth = removeWidth + s.Px(8) + cancelWidth;
                    row.Size = row.GetPreferredSize(Size.Empty);
                    row.PerformLayout();
                    Assert.False(row.IsStacked);
                    Assert.Equal(remove.Top, cancel.Top);
                }
            });
        }

        [Fact]
        public void Wrapped_text_in_a_text_flow_or_a_label_pair_shows_whole_and_starts_on_the_edge()
        {
            // At 150% text the first run's limit sentence took one line and the flow's edge cut "way." off (the label sized
            // itself to its unwrapped text whatever bounds the flow gave it), and " · 2 selected" on a line of its own started
            // a space right of the count.
            Sta.Run(() =>
            {
                using (var f = new Form { FormBorderStyle = FormBorderStyle.FixedSingle, ShowInTaskbar = false, ClientSize = new Size(600, 300) })
                {
                    var ramp = new TypeRamp(9f);
                    var sentence = new ThemedLabel(ramp, Strings.FirstRunLimit) { Tight = true };
                    var why = new ThemedLink(ramp, Strings.FirstRunWhyLink);
                    int sentenceWidth = sentence.GetPreferredSize(Size.Empty).Width;
                    var flow = new TextFlow(sentence, why) { Location = new Point(10, 10), MaximumSize = new Size(sentenceWidth * 2 / 3, 0) };
                    var count = new ThemedLabel(ramp, "Showing 928 of 928", TextStyle.Body, TextRole.Secondary);
                    var suffix = new ThemedLabel(ramp, Strings.FixSelectedSuffix(2), TextStyle.Body, TextRole.Secondary);
                    var pair = new LabelPair(count, suffix);
                    f.Controls.Add(flow);
                    f.Controls.Add(pair);
                    Sta.ShowOffScreen(f);

                    // The sentence wraps in the flow's width and is as tall as its two lines; "Why?" follows its last line.
                    Assert.True(flow.Width <= sentenceWidth * 2 / 3);
                    Assert.True(sentence.Right <= flow.Width, "the sentence runs past the flow");
                    Assert.Equal(sentence.GetPreferredSize(new Size(sentence.Width, 0)).Height, sentence.Height);
                    Assert.True(sentence.Height >= 2 * sentence.Font.Height, "the sentence didn't wrap");
                    Assert.True(why.Bottom <= flow.Height && why.Top >= sentence.Top + sentence.Font.Height - why.Padding.Top);
                    // A new text lays the flow out again: one line, "Why?" after it on that line.
                    sentence.Text = "Short.";
                    Assert.Equal(sentence.GetPreferredSize(Size.Empty), sentence.Size);
                    Assert.True(why.Left >= sentence.Right && why.Top < sentence.Bottom, "Why? doesn't follow the short sentence");

                    // Beside the count when the pair is wide enough; on a line of its own with its letters on the count's
                    // edge otherwise (the space it starts with sits left of the pair).
                    int countWidth = count.GetPreferredSize(Size.Empty).Width;
                    pair.SetBounds(10, 120, 1000, 100);
                    pair.PerformLayout();
                    Assert.Equal(count.Right, suffix.Left);
                    Assert.Equal(count.Top, suffix.Top);
                    pair.SetBounds(10, 120, countWidth + 4, 100);
                    pair.PerformLayout();
                    int space;
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        space = TextRenderer.MeasureText(g, "a a", count.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width -
                            TextRenderer.MeasureText(g, "aa", count.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    Assert.Equal(count.Bottom, suffix.Top);
                    Assert.InRange(count.Left - suffix.Left, space - 1, space + 1);
                    Assert.True(suffix.Right <= pair.Width, "the suffix runs past the pair");

                    // A count that wraps shows whole: at 150% text "No blocked address matches “example-shop”." lost its
                    // search text (the label sized itself to its unwrapped line and the pair's edge cut the rest off).
                    suffix.Visible = false;
                    count.Text = Strings.FixNoMatch("example-shop");
                    int noMatchWidth = count.GetPreferredSize(Size.Empty).Width;
                    pair.SetBounds(10, 120, noMatchWidth * 2 / 3, 100);
                    pair.PerformLayout();
                    Assert.True(count.Height >= 2 * count.Font.Height, "the count didn't wrap");
                    Assert.Equal(count.GetPreferredSize(new Size(count.Width, 0)).Height, count.Height);
                    Assert.True(count.Right <= pair.Width, "the count runs past the pair");
                    Assert.Equal(count.Height, pair.GetPreferredSize(new Size(pair.Width, 0)).Height);
                    // A new text lays the pair out again: one line.
                    count.Text = "Showing 3 of 928";
                    Assert.Equal(count.GetPreferredSize(Size.Empty), count.Size);
                }
            });
        }

        [Fact]
        public void A_footer_takes_the_height_its_text_needs_and_keeps_its_buttons_whole()
        {
            // The Fix dialog's footer kept its old height when only its text changed, and cut Allow, Block again and Close off
            // at the window's bottom edge (visual-1, a11y-1); in the wrapping mode its text stays beside the buttons in at
            // least two lines, so one or two lines of text never change its height (visual-2).
            Sta.Run(() =>
            {
                using (var f = new Form { FormBorderStyle = FormBorderStyle.FixedSingle, ShowInTaskbar = false })
                {
                    var ramp = new TypeRamp(9f);
                    UiScale s = ramp.Scale;
                    var text = new ThemedLabel(ramp, "Showing 807 of 807", TextStyle.Body, TextRole.Secondary);
                    var a = new ThemedButton(ramp, Strings.AllowButton, ButtonStyle.Accent) { Shield = true };
                    var b = new ThemedButton(ramp, Strings.BlockAgainButton) { Shield = true };
                    var c = new ThemedButton(ramp, Strings.CloseButton);
                    var bar = new CommandBar(ramp) { Dock = DockStyle.Bottom };
                    bar.SetLeft(text);
                    bar.SetRight(a, b, c);
                    f.ClientSize = new Size(s.Px(760), s.Px(300));
                    f.Controls.Add(bar);
                    Sta.ShowOffScreen(f);

                    void AssertWhole(string what)
                    {
                        Assert.Equal(bar.GetPreferredSize(new Size(bar.Width, 0)).Height, bar.Height);
                        Assert.Equal(f.ClientSize.Height, bar.Bottom);
                        foreach (Control x in new Control[] { text, a, b, c })
                            Assert.True(x.Top >= 0 && x.Bottom <= bar.ClientSize.Height - bar.Padding.Bottom, what + ": " + x.Text + " is cut off");
                    }

                    // Plain mode (2.8.16): a long text stacks above the buttons, and the bar grows with it.
                    AssertWhole("one line");
                    int oneRow = bar.Height;
                    text.Text = Strings.FixNoMatch("my-favorite-recipes.example and another-shop.example and a third one");
                    bar.RefreshLayout();
                    Assert.True(bar.IsStacked);
                    Assert.True(bar.Height > oneRow);
                    AssertWhole("stacked");
                    text.Text = "Showing 807 of 807";
                    bar.RefreshLayout();
                    Assert.Equal(oneRow, bar.Height);

                    // Wrapping mode: the text wraps beside the buttons, at least two Body lines tall.
                    bar.LeftWrapMinWidth = s.Px(200);
                    bar.LeftReserve = room => 2 * ramp.LineHeight(TextStyle.Body);
                    AssertWhole("wrapping, one line");
                    int twoLines = bar.Height;
                    text.Text = Strings.FixConsequence;
                    bar.RefreshLayout();
                    Assert.False(bar.IsStacked);
                    Assert.Equal(twoLines, bar.Height);
                    Assert.True(text.Height > ramp.LineHeight(TextStyle.Body), "the consequence didn't wrap beside the buttons");
                    AssertWhole("wrapping, two lines");
                    // Three lines and more: the bar grows, its buttons stay whole.
                    text.Text = Strings.ChangingHostsFile + " " + Strings.ChangingSlow;
                    bar.RefreshLayout();
                    Assert.True(bar.Height > twoLines);
                    AssertWhole("wrapping, three lines");
                    // Too little room beside the buttons: above them, whatever the text.
                    f.ClientSize = new Size(a.Width + b.Width + c.Width + 2 * s.Px(8) + s.Px(48) + s.Px(16) + s.Px(150), f.ClientSize.Height);
                    bar.RefreshLayout();
                    Assert.True(bar.IsStacked);
                    AssertWhole("narrow");
                    f.Close();
                    ramp.Dispose();
                }
            });
        }

        // ---------------------------------------------------------------- T-A-26

        [Fact]
        public void The_progress_ring_animates_only_while_active_and_visible()
        {
            Sta.Run(() =>
            {
                try
                {
                    ProgressRing.AnimationsOverride = true;
                    using (var w = new TestWindow())
                    {
                        ProgressRing ring = w.Add(new ProgressRing { AccessibleName = Strings.ChangingHostsFile });
                        Sta.ShowOffScreen(w);
                        Assert.False(ring.TimerRunning);
                        ring.Active = true;
                        Assert.True(ring.Animates);
                        Assert.True(ring.TimerRunning);
                        ring.Visible = false;
                        Assert.False(ring.TimerRunning);
                        ring.Visible = true;
                        Assert.True(ring.TimerRunning);
                        ring.Active = false;
                        Assert.False(ring.TimerRunning);
                        using (Bitmap idle = Pixels.Of(ring)) Assert.Equal(0, Ink(idle, ThemeManager.Current.WindowBackground));

                        // Windows' animation effects off: the static sync glyph, no timer (read at each activation).
                        ProgressRing.AnimationsOverride = false;
                        ring.Active = true;
                        Assert.False(ring.Animates);
                        Assert.False(ring.TimerRunning);
                        using (Bitmap still = Pixels.Of(ring)) Assert.True(Ink(still, ThemeManager.Current.WindowBackground) > 10);
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        private static int Ink(Bitmap bmp, Color background)
        {
            int ink = 0, back = Pixels.Rgb(background);
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (Pixels.Rgb(bmp, x, y) != back) ink++;
            return ink;
        }

        // ---------------------------------------------------------------- T-A-27

        [Theory]
        [InlineData(9f)]
        [InlineData(13.5f)]
        public void Pills_are_a_caption_line_tall_and_padded_8_epx(float basePoints)
        {
            using (var ramp = new TypeRamp(basePoints))
            {
                UiScale s = ramp.Scale;
                int height = Math.Max(s.Px(Metrics.PillHeight), ramp.LineHeight(TextStyle.Caption) + s.Px(4));
                Assert.Equal(height, Pill.Height(ramp));
                foreach (string text in new[] { Strings.StatusAllowed, Strings.StatusBlocked, Strings.TierName(QuietHosts.Core.Tier.Strict), "Recommended" })
                {
                    // The text as it is drawn (NoPadding, on a device context), plus 8 epx on each side.
                    int textWidth;
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        textWidth = TextRenderer.MeasureText(g, text, ramp.Caption, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    Assert.Equal(new Size(Math.Max(height, textWidth + 2 * s.Px(Metrics.PillPadX)), height), Pill.Measure(ramp, text));
                }
            }
            using (var normal = new TypeRamp(9f))
            using (var large = new TypeRamp(13.5f))
            {
                Assert.True(Pill.Measure(large, Strings.StatusBlocked).Width > Pill.Measure(normal, Strings.StatusBlocked).Width);
                Assert.True(Pill.Height(large) > Pill.Height(normal));
            }
        }

        // ---------------------------------------------------------------- T-A-29

        [Fact]
        public void Page_keys_scroll_a_window_whose_content_scrolls_and_leave_the_focus_alone()
        {
            Sta.Run(() =>
            {
                using (var d = new TestDialog(40) { PreviewWorkingArea = UiScenarios.ShortScreen })
                {
                    Sta.ShowOffScreen(d);
                    Assert.True(d.ContentScrolls);
                    ScrollHost host = d.Host!;
                    Assert.True(host.ScrollRange > host.ClientSize.Height);
                    Assert.Same(d.CloseControl, d.ActiveControl);
                    int step = host.ClientSize.Height - d.BodyLine;

                    Assert.True(d.ScrollByKey(Keys.PageDown));
                    Assert.Equal(step, host.ScrollTop);
                    Assert.True(d.ScrollByKey(Keys.PageUp));
                    Assert.Equal(0, host.ScrollTop);
                    Assert.True(d.ScrollByKey(Keys.Control | Keys.End));
                    Assert.Equal(host.ScrollRange, host.ScrollTop);
                    Assert.True(d.ScrollByKey(Keys.Control | Keys.Home));
                    Assert.Equal(0, host.ScrollTop);
                    Assert.Same(d.CloseControl, d.ActiveControl);

                    // The window's own key handling routes the keys there.
                    MethodInfo cmd = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var msg = new Message();
                    Assert.True((bool)cmd.Invoke(d, new object[] { msg, Keys.PageDown })!);
                    Assert.Equal(step, host.ScrollTop);
                    d.ScrollByKey(Keys.Control | Keys.Home);

                    // Other keys and other modifiers are not scrolling keys.
                    Assert.False(d.ScrollByKey(Keys.Home));
                    Assert.False(d.ScrollByKey(Keys.Shift | Keys.PageDown));
                    Assert.False(d.ScrollByKey(Keys.Down));

                    // In a multi-line text box the keys are the text box's own.
                    d.ActiveControl = d.Notes;
                    Assert.False(d.ScrollByKey(Keys.PageDown));
                    Assert.False(d.ScrollByKey(Keys.Control | Keys.End));
                    Assert.Equal(0, host.ScrollTop);
                }

                // A window that fits doesn't scroll and ignores the keys.
                using (var d = new TestDialog(2) { PreviewWorkingArea = Sta.LargeScreen })
                {
                    Sta.ShowOffScreen(d);
                    Assert.False(d.ContentScrolls);
                    Assert.False(d.ScrollByKey(Keys.PageDown));
                    Assert.False(d.ScrollByKey(Keys.Control | Keys.End));
                }
            });
        }

        [Fact]
        public void A_dialog_has_the_dialog_role_its_message_on_the_first_focus_and_a_frame_only_in_high_contrast()
        {
            Sta.Run(() =>
            {
                try
                {
                    foreach (string name in new[] { "light", "dark", "hc-black", "hc-white" })
                    {
                        ThemeManager.Override(Theme.FromName(name)!);
                        Theme t = ThemeManager.Current;
                        using (var d = new TestDialog(2) { PreviewWorkingArea = Sta.LargeScreen })
                        {
                            Sta.ShowOffScreen(d);
                            Assert.Equal(AccessibleRole.Dialog, d.AccessibleRole);
                            Assert.Equal("Test dialog. Paragraphs.", d.CloseControl.ContextDescription);
                            Assert.Equal(FormBorderStyle.FixedDialog, d.FormBorderStyle);
                            Assert.False(d.ShowInTaskbar);
                            int frame = t.IsHighContrast ? UiScale.Current.Stroke(2) : 0;
                            Assert.Equal(new Padding(frame), d.Padding);
                            using (var bmp = new Bitmap(d.ClientSize.Width, d.ClientSize.Height, PixelFormat.Format32bppArgb))
                            {
                                using (Graphics g = Graphics.FromImage(bmp))
                                {
                                    IntPtr hdc = g.GetHdc();
                                    try
                                    {
                                        // The client area only (the frame isn't part of the test).
                                        SendMessage(d.Handle, 0x0318 /* WM_PRINTCLIENT */, hdc, new IntPtr(0x4 | 0x10 /* PRF_CLIENT | PRF_CHILDREN */));
                                    }
                                    finally
                                    {
                                        g.ReleaseHdc(hdc);
                                    }
                                }
                                int edge = Pixels.Rgb(bmp, 0, bmp.Height / 2);
                                if (t.IsHighContrast) Assert.Equal(Pixels.Rgb(t.TextPrimary), edge);
                                else Assert.NotEqual(Pixels.Rgb(t.TextPrimary), edge);
                            }
                        }
                    }
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
