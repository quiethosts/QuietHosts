using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>The level table (spec 3.6) as a pure function.</summary>
    public class LevelUiTests
    {
        private static readonly BlockLists Lists = BlockLists.LoadEmbedded();

        private static void Card(CardView view, Level level, LevelCardState state, bool shield, bool enabled, string name)
        {
            Assert.Equal(level, view.Level);
            Assert.Equal(state, view.State);
            Assert.Equal(state == LevelCardState.None, view.ShowsButton);
            Assert.Equal(shield, view.Shield);
            Assert.Equal(enabled, view.Enabled);
            Assert.Equal(name, view.Name);
            if (view.ShowsButton) Assert.Equal(level == Level.Strict ? Strings.UseStrictCardButton : Strings.UseStandardCardButton, view.ButtonText);
            else Assert.Equal(state == LevelCardState.InUse ? Strings.InUseIndicator : Strings.SelectedIndicator, view.IndicatorText);
        }

        // ---------------------------------------------------------------- T-B-07

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_level_cards_follow_the_level_table(bool canStart)
        {
            // On: the level in the file is in use; "Use Standard" prompts at once (the shield), "Use Strict…" asks first.
            (CardView standard, CardView strict, bool note) = LevelUi.For(UiScenarios.Status(Lists, ProtectionState.On), canStart);
            Card(standard, Level.Standard, LevelCardState.InUse, false, false, "Standard, recommended, in use");
            Card(strict, Level.Strict, LevelCardState.None, false, canStart, "Strict");
            Assert.False(note);
            (standard, strict, note) = LevelUi.For(UiScenarios.Status(Lists, ProtectionState.On, Level.Strict, allowed: 3), canStart);
            Card(standard, Level.Standard, LevelCardState.None, true, canStart, "Standard, recommended");
            Card(strict, Level.Strict, LevelCardState.InUse, false, false, "Strict, in use");
            Assert.False(note);

            // Off, Older list, Removed: only a choice for the next Turn on, without a shield.
            foreach (ProtectionState state in new[] { ProtectionState.Off, ProtectionState.OlderList, ProtectionState.Removed })
            {
                (standard, strict, note) = LevelUi.For(UiScenarios.Status(Lists, state), canStart);
                Card(standard, Level.Standard, LevelCardState.Selected, false, false, "Standard, recommended, selected");
                Card(strict, Level.Strict, LevelCardState.None, false, canStart, "Strict");
                Assert.False(note);
                (standard, strict, note) = LevelUi.For(UiScenarios.Status(Lists, state, pendingLevel: Level.Strict), canStart);
                Card(standard, Level.Standard, LevelCardState.None, false, canStart, "Standard, recommended");
                Card(strict, Level.Strict, LevelCardState.Selected, false, false, "Strict, selected");
                Assert.False(note);
            }

            // Can't read and Refused: "Selected" on the selected level, the other button disabled, and the note.
            foreach (HostsStatus s in new[]
            {
                UiScenarios.Status(Lists, ProtectionState.CantRead),
                UiScenarios.Status(Lists, ProtectionState.Refused, refusal: RefusalReason.TooLarge),
                UiScenarios.Status(Lists, ProtectionState.Refused, Level.Strict, refusal: RefusalReason.HostsPathMoved),
            })
            {
                (standard, strict, note) = LevelUi.For(s, canStart);
                bool strictSelected = s.SelectedLevel == Level.Strict;
                Card(standard, Level.Standard, strictSelected ? LevelCardState.None : LevelCardState.Selected, false, false,
                    strictSelected ? "Standard, recommended" : "Standard, recommended, selected");
                Card(strict, Level.Strict, strictSelected ? LevelCardState.Selected : LevelCardState.None, false, false, strictSelected ? "Strict, selected" : "Strict");
                Assert.True(note);
            }
        }

        [Fact]
        public void The_card_names_and_texts_follow_the_spec()
        {
            Assert.Equal("Standard, recommended, in use", Strings.LevelCardName(Level.Standard, LevelCardState.InUse));
            Assert.Equal("Standard, recommended, selected", Strings.LevelCardName(Level.Standard, LevelCardState.Selected));
            Assert.Equal("Standard, recommended", Strings.LevelCardName(Level.Standard, LevelCardState.None));
            Assert.Equal("Strict, in use", Strings.LevelCardName(Level.Strict, LevelCardState.InUse));
            Assert.Equal("Strict, selected", Strings.LevelCardName(Level.Strict, LevelCardState.Selected));
            Assert.Equal("Strict", Strings.LevelCardName(Level.Strict, LevelCardState.None));
            Assert.Equal("Use St&rict…", Strings.UseStrictCardButton);
            Assert.Equal("Use Stan&dard", Strings.UseStandardCardButton);
            Assert.Equal("&What Strict changes", Strings.WhatStrictChangesLink);
            Assert.Equal("Standard is selected. QuietHosts uses it when you turn protection on.", Strings.LevelSavedAnnouncement(Level.Standard));
        }
    }

    /// <summary>The level cards in the main window (spec 3.6) and the Strict flow (6.2).</summary>
    [Collection(WindowCollection.Name)]
    public class LevelCardTests
    {
        // ---------------------------------------------------------------- T-B-08: the Strict flow

        [Fact]
        public void Use_strict_asks_first_and_prompts_only_while_on()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var heard = new List<(string Text, Announcement Kind)>();
                    global::QuietHosts.Ui.AnnouncementSink = (c, t, k) => heard.Add((t, k));
                    try
                    {
                        HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                        foreach (DialogResult answer in new[] { DialogResult.Cancel, DialogResult.OK })
                        {
                            var commands = new List<ElevatedCommand>();
                            var c = new AppController(p.Context, on, Flow.Returns(ResultKind.Declined, commands), () => on);
                            using (var f = new MainForm(c, showFirstRun: false))
                            using (var watch = new DialogWatch(d => answer))
                            {
                                watch.Describe = d => d is StrictDialog sd ? sd.Mode + (sd.Elevates ? " with the shield" : "") : d.GetType().Name;
                                Sta.ShowOffScreen(f);
                                Flow.Click(f, f.PreviewStrictCard.Button);
                                Flow.PumpUntil(() => watch.Seen.Count > 0 && (answer == DialogResult.Cancel || (c.LastResult != null && !c.IsBusy)));
                                Flow.PumpFor(100);
                                Assert.Equal(new[] { "Confirm with the shield" }, watch.Answered);
                                if (answer == DialogResult.Cancel)
                                {
                                    // Cancel changes nothing: the card stays as it was.
                                    Assert.Empty(commands);
                                    Assert.Null(c.LastResult);
                                }
                                else
                                {
                                    ElevatedCommand cmd = Assert.Single(commands);
                                    Assert.Equal(Verb.On, cmd.Verb);
                                    Assert.Equal(Level.Strict, cmd.Level);
                                    Assert.Equal(ChangeKind.ChangeLevel, c.LastResult!.Kind);
                                }
                                f.Close();
                            }
                        }

                        // Off: the confirmation has no shield, and OK only saves the level (no prompt) and says so.
                        HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                        HostsStatus strictSelected = UiScenarios.Status(p.Lists, ProtectionState.Off, pendingLevel: Level.Strict);
                        var launches = new List<ElevatedCommand>();
                        var offController = new AppController(p.Context, off, Flow.Returns(ResultKind.Success, launches), () => strictSelected);
                        heard.Clear();
                        using (var f = new MainForm(offController, showFirstRun: false) { PreviewForeground = true })
                        using (var watch = new DialogWatch(d => DialogResult.OK))
                        {
                            watch.Describe = d => d is StrictDialog sd ? sd.Mode + (sd.Elevates ? " with the shield" : "") : d.GetType().Name;
                            Sta.ShowOffScreen(f);
                            f.PreviewStrictCard.Button.Focus();
                            // What a screen reader gets, in order: the focus moves to the main button before the pressed button
                            // turns into "Selected" (nothing else gets the focus on the way), and the announcement comes last, so
                            // no focus change cuts it off (a11y-10).
                            var events = new List<string>();
                            foreach (Control c in ControlTree.All(f)) c.GotFocus += (o, e) => events.Add("focus " + ((Control)o!).Text);
                            global::QuietHosts.Ui.AnnouncementSink = (c, t, k) => { heard.Add((t, k)); events.Add("announce " + t); };
                            Flow.Click(f, f.PreviewStrictCard.Button);
                            Flow.PumpUntil(() => offController.Status == strictSelected && heard.Count > 0);
                            Flow.PumpFor(50);
                            Assert.Equal(new[] { "Confirm" }, watch.Answered);
                            Assert.Empty(launches);
                            Assert.Null(offController.LastResult);
                            Assert.Equal(new[] { (Strings.LevelSavedAnnouncement(Level.Strict), Announcement.Result) }, heard);
                            Assert.Equal(LevelCardState.Selected, f.PreviewStrictCard.View!.State);
                            Assert.Equal("Strict, selected", f.PreviewStrictCard.AccessibleName);
                            // The pressed button became "Selected": the focus goes to the main button.
                            Assert.True(f.PreviewStatusCard.MainButton.Focused);
                            Assert.Equal(new[] { "focus " + f.PreviewStatusCard.MainButton.Text, "announce " + Strings.LevelSavedAnnouncement(Level.Strict) },
                                events.Where(e => e != "focus " + f.PreviewStrictCard.Button.Text).ToArray());

                            // "Use Standard" while off: no confirmation, no prompt.
                            Flow.Click(f, f.PreviewStandardCard.Button);
                            Flow.PumpUntil(() => heard.Count > 1);
                            Assert.Equal((Strings.LevelSavedAnnouncement(Level.Standard), Announcement.Result), heard[1]);
                            Assert.Single(watch.Seen);
                            Assert.Empty(launches);

                            // "What Strict changes": the same text with Close only.
                            ((ThemedLink)f.PreviewStrictCard.Link!).PerformClick();
                            Assert.Equal(new[] { "Confirm", "Info" }, watch.Answered);
                            f.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        [Fact]
        public void A_level_change_while_on_needs_no_confirmation_to_go_back_to_standard()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus strict = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3);
                    var commands = new List<ElevatedCommand>();
                    var c = new AppController(p.Context, strict, Flow.Returns(ResultKind.Declined, commands), () => strict);
                    using (var f = new MainForm(c, showFirstRun: false))
                    using (var watch = new DialogWatch(d => DialogResult.Cancel))
                    {
                        Sta.ShowOffScreen(f);
                        Assert.True(f.PreviewStandardCard.Button.Shield);
                        Flow.Click(f, f.PreviewStandardCard.Button);
                        Flow.PumpUntil(() => c.LastResult != null && !c.IsBusy);
                        Assert.Empty(watch.Seen);
                        ElevatedCommand cmd = Assert.Single(commands);
                        Assert.Equal(Level.Standard, cmd.Level);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- the card layout (3.6, D19)

        [Fact]
        public void The_descriptions_take_one_line_at_the_normal_text_size()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
#if DEBUG
                        UiFonts.TextScale = 1f;
#endif
                        using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            foreach (LevelCard card in new[] { f.PreviewStandardCard, f.PreviewStrictCard })
                            {
                                Assert.False(card.IsStacked);
                                // One line: less than two lines of the font (D19 b).
                                Assert.True(card.Description.Height < 2 * card.Description.Font.Height, card.Level + "'s description wraps: " + card.Description.Height + " px");
                                Assert.Equal(card.Title.Left, card.Description.Left);
                            }
                            // Both left columns are as wide: the right columns are too.
                            Assert.Equal(f.PreviewStandardCard.RightWidth, f.PreviewStrictCard.RightWidth);
                            Assert.True(f.PreviewStandardCard.RightWidth >= UiScale.Current.Px(Metrics.LevelRightMinWidth));
                            // "What Strict changes" lines its text up with the description (its box has a 4 epx padding first).
                            ThemedLink link = f.PreviewStrictCard.Link!;
                            Assert.Equal(f.PreviewStrictCard.Description.Left, link.Left + link.Padding.Left);
                            // Side by side, the button comes first in the tab order, then the link (3.8).
                            Assert.True(f.PreviewStrictCard.Button.TabIndex < link.TabIndex);
                            Assert.Equal(Strings.RecommendedTag, f.PreviewStandardCard.Title.PillText);
                            Assert.Null(f.PreviewStrictCard.Title.PillText);
                            Assert.Equal("Standard, recommended", f.PreviewStandardCard.Title.AccessibleName);
                            f.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

#if DEBUG
        [Fact]
        public void At_the_largest_text_sizes_the_cards_stack()
        {
            // 3.6: a card stacks when its left column would be narrower than 240 epx, and (fixer) narrower than the Standard
            // title row, which can't wrap: at 225% text the left column came out 241 px beside "Use Standard", the title broke
            // into a letter or two per line beside its tag, and the description started some 300 px lower. Both cards decide
            // alike; at 250% they certainly stack.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        UiScale s = UiScale.Current;
                        foreach (float text in new[] { 1.5f, 1.75f, 2f, 2.25f, 2.5f })
                        {
                            UiFonts.TextScale = text;
                            foreach (ProtectionState state in new[] { ProtectionState.On, ProtectionState.Off })
                            {
                                using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, state, Level.Strict)), showFirstRun: false))
                                {
                                    Sta.ShowOffScreen(f);
                                    string what = text + "x text, " + state + ": ";
                                    LevelCard standard = f.PreviewStandardCard, strict = f.PreviewStrictCard;
                                    int left = standard.Width - 2 * s.Px(Metrics.CardPadding) - s.Px(Metrics.TextToButton) - standard.RightWidth;
                                    int titleRow = standard.Title.GetPreferredSize(System.Drawing.Size.Empty).Width;
                                    Assert.Equal(left < Math.Max(s.Px(240), titleRow), standard.IsStacked);
                                    Assert.Equal(standard.IsStacked, strict.IsStacked);
                                    if (text >= 2.5f) Assert.True(standard.IsStacked, what + "not stacked");
                                    foreach (LevelCard card in new[] { standard, strict })
                                    {
                                        // The title row takes one line, and the description follows it 4 epx below.
                                        Assert.True(card.Title.Height < 2 * card.Title.Font.Height, what + card.Level + "'s title takes " + card.Title.Height + " px");
                                        Assert.Equal(card.Title.Bottom + s.Px(4), card.Description.Top);
                                        Control side = card.Button.Visible ? (Control)card.Button : card.Indicator;
                                        if (card.IsStacked)
                                        {
                                            // The right side goes 8 epx below the left column, left-aligned with it.
                                            Control last = card.Link ?? (Control)card.Description;
                                            Assert.Equal(card.Description.Left, side.Left);
                                            Assert.Equal(last.Bottom + s.Px(Metrics.HeadingGap), side.Top);
                                        }
                                        Assert.True(side.Bottom <= card.ClientSize.Height - s.Px(Metrics.CardPadding), what + card.Level + "'s right side runs past the card");
                                    }
                                    f.Close();
                                }
                            }
                        }
                        // Stacked, "What Strict changes" sits above "Use Strict…", so Tab reaches it first (K6: tab order is
                        // reading order, a11y-7).
                        UiFonts.TextScale = 2.5f;
                        using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            LevelCard strict = f.PreviewStrictCard;
                            Assert.True(strict.IsStacked);
                            Assert.True(strict.Link!.Bottom <= strict.Button.Top);
                            Assert.True(strict.Link.TabIndex < strict.Button.TabIndex);
                            f.PreviewStatusCard.MainButton.Focus();
                            Flow.SendKey(f, Keys.Tab);
                            Assert.True(strict.Link.Focused, "Tab from the main button didn't reach What Strict changes first");
                            Flow.SendKey(f, Keys.Tab);
                            Assert.True(strict.Button.Focused);
                            f.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }
#endif
    }
}
