using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// The redesigned Fix dialog (spec 5, T-C-01..13 and T-C-21): the step cards by state, the footer, the columns, the cells,
    /// the details line, the empty state, the search, Allow and Block again, the Enter rule, the result bar, what screen
    /// readers hear, the site report, and the dialog at large text and on short screens.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class FixDialogTests
    {
        private static bool Shows(Form f, Control c) => ControlTree.IsMeantToShow(c, f);

        private const int GWL_STYLE = -16, WS_HSCROLL = 0x00100000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetWindowLong(IntPtr window, int index);

        /// <summary>Whether the native list shows its horizontal scroll bar.</summary>
        private static bool ScrollsSideways(ListView list) => (GetWindowLong(list.Handle, GWL_STYLE) & WS_HSCROLL) != 0;

        // ---------------------------------------------------------------- T-C-01: the step cards by state (5.3)

        public static IEnumerable<object[]> StepStates() => new[]
        {
            // name, last card's title, body, button (null: none), accent
            new object[] { "on-strict", Strings.FixStepTestTitle, Strings.FixStepTestBody, Strings.FixTurnOffButton, false },
            new object[] { "on-standard", Strings.FixStepTestTitle, Strings.FixStepTestBody, Strings.FixTurnOffButton, false },
            new object[] { "cant-read", Strings.FixStepCantChangeTitle, Strings.FixStepCantChangeBody, null!, false },
            new object[] { "refused", Strings.FixStepCantChangeTitle, Strings.FixStepCantChangeBody, null!, false },
            new object[] { "older-list", Strings.FixStepOlderListTitle, Strings.FixStepOlderListBody, Strings.FixReplaceOlderListButton, true },
            new object[] { "off-with-older-list", Strings.FixStepOlderListTitle, Strings.FixStepOlderListBody, Strings.FixReplaceOlderListButton, true },
            new object[] { "removed-with-older-list", Strings.FixStepOlderListTitle, Strings.FixStepOlderListBody, Strings.FixReplaceOlderListButton, true },
            new object[] { "off-after-test", Strings.FixStepOffTitle, Strings.FixStepOffAfterTestBody, Strings.FixTurnOnButton, true },
            new object[] { "removed", Strings.FixStepOffTitle, Strings.FixStepOffBody, Strings.FixTurnOnButton, false },
            new object[] { "off", Strings.FixStepOffTitle, Strings.FixStepOffBody, Strings.FixTurnOnProtectionButton, false },
        };

        private static AppController ControllerFor(PreviewContext p, string name, LaunchChange? launch = null, Func<HostsStatus>? read = null)
        {
            BlockLists l = p.Lists;
            HostsStatus s;
            switch (name)
            {
                case "on-strict": s = UiScenarios.Status(l, ProtectionState.On, Level.Strict, allowed: 3); break;
                case "on-standard": s = UiScenarios.Status(l, ProtectionState.On); break;
                case "cant-read": s = UiScenarios.Status(l, ProtectionState.CantRead); break;
                case "refused": s = UiScenarios.Status(l, ProtectionState.Refused, refusal: RefusalReason.TooLarge); break;
                case "older-list": s = UiScenarios.Status(l, ProtectionState.OlderList); break;
                case "off-with-older-list": s = UiScenarios.Status(l, ProtectionState.Off, legacy: true); break;
                case "removed-with-older-list": s = UiScenarios.Status(l, ProtectionState.Removed, legacy: true, warnings: HostsWarnings.LegacyBlockToo); break;
                case "removed": s = UiScenarios.Status(l, ProtectionState.Removed); break;
                default: s = UiScenarios.Status(l, ProtectionState.Off); break;
            }
            var c = launch == null ? new AppController(p.Context, s) : new AppController(p.Context, s, launch, read);
            if (name == "off-after-test") c.PreviewOffForTest();
            return c;
        }

        [Theory]
        [MemberData(nameof(StepStates))]
        public void The_step_cards_follow_the_state_table(string state, string title, string body, string? button, bool accent)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(ControllerFor(p, state)))
                    {
                        Sta.ShowOffScreen(d);
                        bool strict = state == "on-strict";
                        IReadOnlyList<StepCard> cards = d.PreviewStepCards;
                        // Card 1 always; card 2 (Try Standard) only on Strict; the last card numbered after them.
                        Assert.True(Shows(d, cards[0]));
                        Assert.Equal(1, cards[0].Number);
                        Assert.Equal(Strings.FixStep1Title, cards[0].Title);
                        Assert.Equal(Strings.FixStep1Body, cards[0].BodyText);
                        Assert.Null(cards[0].Button);
                        Assert.Equal(Strings.FixStepName(1, Strings.FixStep1Title), cards[0].AccessibleName);
                        Assert.Equal(strict, Shows(d, cards[1]));
                        if (strict)
                        {
                            Assert.Equal(2, cards[1].Number);
                            Assert.Equal(Strings.FixStep2Title, cards[1].Title);
                            Assert.Equal(Strings.FixStep2Body, cards[1].BodyText);
                            ThemedButton standard = d.PreviewSwitchToStandard;
                            Assert.Equal(Strings.FixSwitchToStandardButton, standard.Text);
                            Assert.Equal(ButtonStyle.Standard, standard.Style);
                            Assert.True(standard.Shield);
                            Assert.True(standard.SwallowsEnter);
                            Assert.Equal(Strings.FixStep2Body + " " + Strings.ShieldDescription, standard.AccessibleDescription);
                        }
                        StepCard last = d.PreviewLastCard;
                        int number = strict ? 3 : 2;
                        Assert.Equal(number, last.Number);
                        Assert.Equal(title, last.Title);
                        Assert.Equal(body, last.BodyText);
                        Assert.Equal(Strings.FixStepName(number, title), last.AccessibleName);
                        Assert.Equal(AccessibleRole.Grouping, last.AccessibilityObject.Role);
                        ThemedButton b = d.PreviewLastButton;
                        Assert.Equal(button != null, Shows(d, b));
                        if (button != null)
                        {
                            Assert.Equal(button, b.Text);
                            Assert.Equal(accent ? ButtonStyle.Accent : ButtonStyle.Standard, b.Style);
                            Assert.True(b.Shield);
                            Assert.True(b.SwallowsEnter);
                            Assert.True(b.Enabled);
                            Assert.Equal(body + " " + Strings.ShieldDescription, b.AccessibleDescription);
                        }
                        // The badge is painted in the card (no control of its own), round, 24 epx at 100% text.
                        Assert.True(last.BadgeBounds.Width >= UiScale.Current.Px(Metrics.StepBadge));
                        Assert.Equal(last.BadgeBounds.Width, last.BadgeBounds.Height);
                        d.Close();
                    }
                });
            }
        }

        [Fact]
        public void The_step_buttons_run_their_change()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach ((string state, Func<FixDialog, Button> find, Verb verb, Level? level, ChangeKind kind) in new (string, Func<FixDialog, Button>, Verb, Level?, ChangeKind)[]
                    {
                        ("on-strict", d => d.PreviewSwitchToStandard, Verb.On, Level.Standard, ChangeKind.ChangeLevel),
                        ("on-standard", d => d.PreviewLastButton, Verb.Off, null, ChangeKind.TurnOffForTest),
                        ("off", d => d.PreviewLastButton, Verb.On, Level.Standard, ChangeKind.TurnOn),
                        ("older-list", d => d.PreviewLastButton, Verb.On, Level.Standard, ChangeKind.TurnOn),
                        ("removed", d => d.PreviewLastButton, Verb.On, Level.Standard, ChangeKind.TurnOn),
                    })
                    {
                        var commands = new List<ElevatedCommand>();
                        AppController c = ControllerFor(p, state, Flow.Returns(ResultKind.Declined, commands));
                        var kinds = new List<ChangeKind>();
                        c.ChangeFinished += (o, e) => kinds.Add(e.Kind);
                        using (var d = new FixDialog(c))
                        {
                            Sta.ShowOffScreen(d);
                            Flow.Click(d, find(d));
                            Flow.PumpUntil(() => kinds.Count > 0 && !c.IsBusy);
                            ElevatedCommand cmd = Assert.Single(commands);
                            Assert.Equal(verb, cmd.Verb);
                            Assert.Equal(level, cmd.Level);
                            Assert.Equal(kind, Assert.Single(kinds));
                            d.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-02: the last button survives its change and keeps the focus

        [Fact]
        public void The_last_step_button_stays_the_same_control_and_keeps_the_focus()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                    AppController c = ControllerFor(p, "on-standard", Flow.Returns(ResultKind.Success), () => off);
                    using (var d = new FixDialog(c) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(d);
                        ThemedButton step = d.PreviewLastButton;
                        Assert.Equal(Strings.FixTurnOffButton, step.Text);
                        step.Focus();
                        Flow.Click(d, step);
                        Flow.PumpUntil(() => c.LastResult != null && !c.IsBusy);
                        // A test turn-off: the card says protection is off after the test, with "Turn on again" (accent).
                        Assert.True(c.OffForTest);
                        Assert.Same(step, d.PreviewLastButton);
                        Assert.False(step.IsDisposed);
                        Assert.Equal(Strings.FixTurnOnButton, step.Text);
                        Assert.Equal(ButtonStyle.Accent, step.Style);
                        Assert.Equal(Strings.FixStepOffAfterTestBody, d.PreviewLastCard.BodyText);
                        Assert.True(step.Focused, "the focus didn't stay on the step button");
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-03: the footer (5.9)

        [Fact]
        public void The_footer_says_the_first_thing_that_applies()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    BlockLists l = p.Lists;
                    // 1: a change in progress here (one line), or in another run.
                    var busy = new AppController(p.Context, UiScenarios.Status(l, ProtectionState.On));
                    using (var d = new FixDialog(busy))
                    {
                        Sta.ShowOffScreen(d);
                        busy.PreviewBusy(Strings.ChangingHostsFile, takingLong: true);
                        Assert.Equal(Strings.ChangingHostsFile + " " + Strings.ChangingSlow, d.FooterText);
                        Assert.True(Shows(d, d.PreviewRing));
                        Assert.True(d.PreviewRing.Active);
                        busy.PreviewClearBusy();
                        Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows), d.FooterText);
                        Assert.False(Shows(d, d.PreviewRing));
                        d.Close();
                    }
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, ProtectionState.CantRead, runInProgress: true))))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(Strings.OtherChangeRunning, d.FooterText);
                        d.Close();
                    }
                    // 2: Can't read or Refused; 3: not on.
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, ProtectionState.Refused, refusal: RefusalReason.HostsPathMoved))))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(Strings.FixCantChange, d.FooterText);
                        d.Close();
                    }
                    foreach (ProtectionState state in new[] { ProtectionState.Off, ProtectionState.Removed, ProtectionState.OlderList })
                    {
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, state))))
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewSelect(2);
                            Assert.Equal(Strings.FixTurnOnToChange, d.FooterText);
                            d.Close();
                        }
                    }
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, ProtectionState.On, Level.Strict, allowed: 3))))
                    {
                        Sta.ShowOffScreen(d);
                        // 6: the count (polite live region), and nothing after it while nothing is selected.
                        Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows), d.FooterText);
                        Assert.Equal(AutomationLiveSetting.Polite, d.PreviewCountLabel.LiveSetting);
                        Assert.Equal(AutomationLiveSetting.Off, d.PreviewSuffixLabel.LiveSetting);
                        Assert.False(Shows(d, d.PreviewSuffixLabel));
                        // Allowed rows only: the count and " · 2 selected" in its own label, which isn't a live region.
                        d.PreviewSelectAllowed(2);
                        Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows) + Strings.FixSelectedSuffix(2), d.FooterText);
                        Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows), d.PreviewCountLabel.Text);
                        Assert.True(Shows(d, d.PreviewSuffixLabel));
                        // On the count's line, right after it.
                        Assert.Equal(d.PreviewCountLabel.Top, d.PreviewSuffixLabel.Top);
                        Assert.Equal(d.PreviewCountLabel.Right, d.PreviewSuffixLabel.Left);
                        // 5: one to 100 selected and one of them blocked: what Allow does.
                        d.PreviewSearch(string.Empty);
                        d.PreviewSelect(5);
                        Assert.Equal(Strings.FixConsequence, d.FooterText);
                        // 4: more than 100.
                        d.PreviewSearch(string.Empty);
                        d.PreviewSelect(Limits.MaxHostsPerRun + 1);
                        Assert.Equal(Strings.FixTooMany(Limits.MaxHostsPerRun), d.FooterText);
                        // A search that matches nothing: the count label says so.
                        d.PreviewSearch("example-shop");
                        Assert.Equal(Strings.FixNoMatch("example-shop"), d.FooterText);
                        d.Close();
                    }
                });
            }
        }

        /// <summary>Every shown footer part lies inside the footer band and inside the dialog's client area.</summary>
        private static void AssertFooterWhole(FixDialog d, string what)
        {
            CommandBar footer = d.PreviewFooter;
            Assert.True(footer.Height >= footer.GetPreferredSize(new Size(footer.Width, 0)).Height, what + ": the footer is shorter than its content");
            foreach (Control c in footer.LeftControls.Concat(footer.RightControls))
            {
                if (!Shows(d, c)) continue;
                Assert.True(c.Top >= 0 && c.Bottom <= footer.ClientSize.Height, what + ": " + c.GetType().Name + " \"" + c.Text + "\" runs past the footer (" + c.Bounds + " in " + footer.ClientSize + ")");
                Rectangle r = d.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                Assert.True(r.Top >= 0 && r.Bottom <= d.ClientSize.Height, what + ": " + c.GetType().Name + " \"" + c.Text + "\" is cut off by the window's edge");
            }
        }

        [Fact]
        public void The_footer_keeps_allow_block_again_and_close_whole_whatever_it_says()
        {
            // The footer kept its old height when only its text changed, so a longer text cut the buttons off at the window's
            // bottom edge (visual-1, a11y-1): a long search with no match, the selected suffix at the smallest width, each
            // progress text.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    BlockLists l = p.Lists;
                    var on = new AppController(p.Context, UiScenarios.Status(l, ProtectionState.On));
                    using (var d = new FixDialog(on))
                    {
                        Sta.ShowOffScreen(d);
                        AssertFooterWhole(d, "the count");
                        d.PreviewSearch("my-favorite-recipes.example and my-other-favorite-recipes.example too");
                        Assert.StartsWith(Strings.FixNoMatch(string.Empty).Substring(0, 10), d.FooterText, StringComparison.Ordinal);
                        AssertFooterWhole(d, "a long search with no match");
                        d.PreviewSearch(string.Empty);
                        foreach ((string progress, bool slow) in new[] { (Strings.WaitingForPermission, false), (Strings.ChangingHostsFile, false), (Strings.ChangingHostsFile, true) })
                        {
                            on.PreviewBusy(progress, slow);
                            AssertFooterWhole(d, progress + (slow ? " (slow)" : string.Empty));
                            on.PreviewClearBusy();
                            AssertFooterWhole(d, "after " + progress);
                        }
                        d.Close();
                    }
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, ProtectionState.On, runInProgress: true))))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(Strings.OtherChangeRunning, d.FooterText);
                        AssertFooterWhole(d, "another run");
                        d.Close();
                    }
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(l, ProtectionState.On, Level.Strict, allowed: 3))))
                    {
                        Sta.ShowOffScreen(d);
                        d.PreviewMinimumSize();
                        Application.DoEvents();
                        d.PreviewSelectAllowed(2);
                        AssertFooterWhole(d, "two allowed rows selected at the smallest size");
                        d.PreviewSearch(string.Empty);
                        d.PreviewSelect(1);
                        AssertFooterWhole(d, "a blocked row selected at the smallest size");
                        d.Close();
                    }
                });
            }
        }

#if DEBUG
        [Fact]
        public void At_large_text_no_column_header_is_cut()
        {
            // At 150% text the Company header read "Compa…": its fixed 96 epx minimum was narrower than its own header (visual-11).
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (float text in new[] { 1f, 1.5f, 2.25f })
                        {
                            UiFonts.TextScale = text;
                            using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                            {
                                Sta.ShowOffScreen(d);
                                UiScale s = UiScale.Current;
                                (int[] widths, int[] min) = d.PreviewColumnWidths();
                                ThemedListView list = d.PreviewList;
                                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                                {
                                    for (int i = 0; i < 5; i++)
                                    {
                                        int header = TextRenderer.MeasureText(g, list.Columns[i].Text, d.Ramp.Caption, new Size(int.MaxValue, int.MaxValue),
                                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + s.Px(i == 0 ? 16 : 12) + s.Px(8);
                                        Assert.True(min[i] >= header, text + "x text: column " + i + "'s minimum " + min[i] + " is narrower than its header " + header);
                                        Assert.True(list.Columns[i].Width >= header, text + "x text: column " + i + " is " + list.Columns[i].Width + " wide, its header " + header);
                                    }
                                }
                                if (text == 1f)
                                {
                                    // At the normal text size the spec's widths stand (5.6).
                                    Assert.Equal(new[] { s.Px(160), s.Px(96), s.Px(120) }, min.Take(3));
                                    Assert.Equal(new[] { s.Px(232), s.Px(128), s.Px(152) }, widths.Take(3));
                                }
                                d.Close();
                            }
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        [Fact]
        public void At_150_percent_text_the_footer_keeps_its_buttons_whole()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        UiFonts.TextScale = 1.5f;
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewSelectAllowed(2);
                            Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows) + Strings.FixSelectedSuffix(2), d.FooterText);
                            AssertFooterWhole(d, "two allowed rows selected");
                            d.PreviewSearch("example-shop");
                            AssertFooterWhole(d, "no match");
                            // The count shows whole when it wraps (it lost the search text: the label sized itself to one
                            // unwrapped line, which the count line's edge cut off).
                            ThemedLabel count = d.PreviewCountLabel;
                            Assert.Equal(Strings.FixNoMatch("example-shop"), count.Text);
                            Assert.True(count.Height >= count.GetPreferredSize(new Size(count.Width, 0)).Height, "the count is cut off below");
                            Assert.True(count.Right <= count.Parent!.ClientSize.Width, "the count runs past its line (" + count.Bounds + " in " + count.Parent.ClientSize + ")");
                            d.Close();
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

        [Fact]
        public void Selecting_a_row_never_moves_the_list()
        {
            // A click on a blocked row swapped the count for the longer consequence, which stacked the footer 28 px taller and
            // moved the list's bottom, the details line and the footer under the pointer (visual-2).
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                    {
                        Sta.ShowOffScreen(d);
                        Rectangle list = d.PreviewListCard.Bounds;
                        int footer = d.PreviewFooter.Height;
                        d.PreviewSelect(4);
                        Assert.Equal(Strings.FixConsequence, d.FooterText);
                        Assert.False(d.PreviewFooter.IsStacked);
                        Assert.Equal(footer, d.PreviewFooter.Height);
                        Assert.Equal(list, d.PreviewListCard.Bounds);
                        d.PreviewSearch(string.Empty);
                        d.PreviewSelectAllowed(2);
                        Assert.Equal(footer, d.PreviewFooter.Height);
                        Assert.Equal(list, d.PreviewListCard.Bounds);
                        d.PreviewSearch(string.Empty);
                        Assert.Equal(footer, d.PreviewFooter.Height);
                        Assert.Equal(list, d.PreviewListCard.Bounds);
                        d.Close();
                    }
                });
            }
        }

        [Fact]
        public void The_selected_suffix_follows_the_count_with_one_space()
        {
            // " · 2 selected" sat 13 px after the count (both labels' blank GDI margins and the space, visual-13): both labels
            // are tight, so only the space shows between "928" and "·".
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        ThemeManager.Override(Theme.Light);
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewSelectAllowed(2);
                            ThemedLabel count = d.PreviewCountLabel, suffix = d.PreviewSuffixLabel;
                            Assert.True(count.Tight && suffix.Tight);
                            Assert.Equal(count.Right, suffix.Left);
                            Assert.Equal(count.Top, suffix.Top);
                            int space;
                            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                                space = TextRenderer.MeasureText(g, "a a", count.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width -
                                    TextRenderer.MeasureText(g, "aa", count.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                            using (Bitmap bmp = Pixels.Of(d.PreviewFooter))
                            {
                                int y0 = count.Parent!.Top + count.Top, y1 = y0 + count.Height;
                                int background = Pixels.Rgb(ThemeManager.Current.WindowBackground);
                                bool Ink(int x)
                                {
                                    for (int y = y0; y < y1; y++)
                                        if (Pixels.Rgb(bmp, x, y) != background) return true;
                                    return false;
                                }
                                int countLeft = count.Parent.Left + count.Left, suffixLeft = count.Parent.Left + suffix.Left;
                                int lastCountInk = -1;
                                for (int x = countLeft; x < suffixLeft + 2; x++) if (Ink(x)) lastCountInk = x;
                                int firstSuffixInk = -1;
                                for (int x = suffixLeft; x < suffixLeft + suffix.Width && firstSuffixInk < 0; x++) if (Ink(x)) firstSuffixInk = x;
                                Assert.True(lastCountInk > 0 && firstSuffixInk > lastCountInk, "no ink found");
                                int gap = firstSuffixInk - lastCountInk - 1;
                                Assert.True(gap <= space + UiScale.Current.Px(3), "the gap is " + gap + " px; a space is " + space);
                            }
                            d.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        [Fact]
        public void A_result_bar_grows_the_dialog_instead_of_scrolling_its_body()
        {
            // At the default size a result bar made the body scroll for 2 to 21 px, and every column narrowed by the scroll
            // bar (visual-3): the dialog grows by what the bar needs and gives it back when the bar goes; the list keeps its rows.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (Level level in new[] { Level.Standard, Level.Strict })
                    {
                        HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, level, allowed: level == Level.Strict ? 3 : 0);
                        var c = new AppController(p.Context, s);
                        using (var d = new FixDialog(c))
                        {
                            Sta.ShowOffScreen(d);
                            Assert.False(d.ContentScrolls);
                            int height = d.Height;
                            c.PreviewResult(UiScenarios.Result(p.Lists, ChangeKind.TurnOffForTest, ElevatedCommand.Off(), ResultKind.Declined, s), d);
                            Application.DoEvents();
                            Assert.True(Shows(d, d.PreviewResultBar));
                            Assert.False(d.ContentScrolls, level + ": the body scrolls with the result bar");
                            Assert.True(d.PreviewListCard.Height >= d.PreviewMinListCardHeight, level + ": the list has fewer than four rows");
                            Assert.InRange(d.Height - height, 0, d.PreviewResultBar.Height + UiScale.Current.Px(12));
                            AssertFooterWhole(d, level + " with a result");
                            // The next change starts: the bar goes, and so does the height it added.
                            c.PreviewBusy(Strings.WaitingForPermission);
                            Application.DoEvents();
                            Assert.False(Shows(d, d.PreviewResultBar));
                            Assert.Equal(height, d.Height);
                            c.PreviewClearBusy();
                            d.Close();
                        }
                    }
                });
            }
        }

        [Fact]
        public void The_count_is_heard_when_it_comes_back_in_place_of_another_text()
        {
            // A search that clears a selection brings the count back in place of the consequence: its live region spoke from
            // a label out of the window, and the dialog didn't say it either (a11y-3).
            using (var p = new PreviewContext())
            {
                var heard = new List<(string Text, Announcement Kind)>();
                Sta.Run(() =>
                {
                    global::QuietHosts.Ui.AnnouncementSink = (c, text, kind) => heard.Add((text, kind));
                    try
                    {
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewSearch("yahoo");
                            d.PreviewSelect(1);
                            Assert.Equal(Strings.FixConsequence, d.FooterText);
                            heard.Clear();
                            d.PreviewSearch("yahoo.com");
                            Assert.Equal(Strings.FixShowing(d.ShownRows, d.TotalRows), d.FooterText);
                            Assert.Equal(new[] { (Strings.FixShowing(d.ShownRows, d.TotalRows), Announcement.Progress) }, heard);
                            // The count showed before and after: its live region says it; the dialog adds nothing.
                            heard.Clear();
                            d.PreviewSearch("yahoo");
                            Assert.Empty(heard);
                            d.Close();
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
        public void Footer_parts_follow_a_theme_change_made_while_they_were_out_of_the_footer()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        ThemeManager.Override(Theme.Light);
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                        {
                            Sta.ShowOffScreen(d);
                            // The count shows; the consequence line is out of the footer when Windows switches to dark mode.
                            Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows), d.FooterText);
                            ThemeManager.Override(Theme.Dark);
                            d.PreviewSelect(1);
                            Assert.Equal(Strings.FixConsequence, d.FooterText);
                            var line = (ThemedLabel)Assert.Single(d.PreviewFooter.LeftControls);
                            Assert.Equal(Pixels.Rgb(Theme.Dark.TextSecondary), Pixels.Rgb(line.ForeColor));
                            d.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        [Fact]
        public void A_search_is_heard_also_while_the_footer_says_something_else()
        {
            using (var p = new PreviewContext())
            {
                var heard = new List<(string Text, Announcement Kind)>();
                Sta.Run(() =>
                {
                    global::QuietHosts.Ui.AnnouncementSink = (c, text, kind) => heard.Add((text, kind));
                    try
                    {
                        // Off: the footer says "Turn protection on…", so the dialog says the count itself, once per applied search.
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off))))
                        {
                            Sta.ShowOffScreen(d);
                            heard.Clear();
                            d.PreviewSearch("yahoo");
                            Assert.Equal(new[] { (Strings.FixShowing(d.ShownRows, d.TotalRows), Announcement.Progress) }, heard);
                            heard.Clear();
                            d.PreviewSearch("example-shop");
                            Assert.Equal(new[] { (Strings.FixNoMatch("example-shop"), Announcement.Progress) }, heard);
                            d.Close();
                        }
                        // On, nothing selected: the count label is shown and is the live region; no announcement of its own.
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                        {
                            Sta.ShowOffScreen(d);
                            heard.Clear();
                            d.PreviewSearch("yahoo");
                            Assert.Empty(heard);
                            Assert.Equal(Strings.FixShowing(d.ShownRows, d.TotalRows), d.PreviewCountLabel.Text);
                            d.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-04: the columns (5.6)

        [Fact]
        public void The_columns_fill_the_list_and_shrink_company_first()
        {
            // The pure fitting: base widths with the rest to Address; when there isn't room Company shrinks first, then What it
            // is, then Address; below the minimums, the minimums.
            int[] widths = { 232, 128, 152, 64, 125 }, min = { 160, 96, 120, 64, 125 };
            Assert.Equal(new[] { 331, 128, 152, 64, 125 }, FixDialog.FitWidths(800, widths, min));
            Assert.Equal(new[] { 232, 107, 152, 64, 125 }, FixDialog.FitWidths(680, widths, min));
            Assert.Equal(new[] { 232, 96, 135, 64, 125 }, FixDialog.FitWidths(652, widths, min));
            Assert.Equal(new[] { 195, 96, 120, 64, 125 }, FixDialog.FitWidths(600, widths, min));
            Assert.Equal(min, FixDialog.FitWidths(500, widths, min));
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                    {
                        Sta.ShowOffScreen(d);
                        ThemedListView list = d.PreviewList;
                        (int[] baseWidths, int[] minWidths) = d.PreviewColumnWidths();
                        int[] Widths() => list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
                        // The default size: the widths fill the list exactly, as the fitting gives them.
                        Assert.Equal(list.ClientSize.Width, Widths().Sum());
                        Assert.Equal(FixDialog.FitWidths(list.ClientSize.Width, baseWidths, minWidths), Widths());
                        Assert.False(ScrollsSideways(list));
                        // Wide: only Address grows.
                        d.ClientSize = new Size(d.ClientSize.Width + UiScale.Current.Px(300), d.ClientSize.Height);
                        Application.DoEvents();
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));
                        for (int i = 1; i < 5; i++) Assert.Equal(baseWidths[i], list.Columns[i].Width);
                        // The smallest size: the fitting's widths; Strict's stacked cards make the body scroll there, and the
                        // narrower list may then need the minimums and a horizontal scroll bar (5.6).
                        d.Size = d.MinimumSize;
                        Application.DoEvents();
                        Assert.Equal(FixDialog.FitWidths(list.ClientSize.Width, baseWidths, minWidths), Widths());
                        Assert.True(list.Columns[1].Width < baseWidths[1], "Company didn't shrink first");
                        // Level and Status keep the widths their pills need.
                        Assert.Equal(baseWidths[3], list.Columns[3].Width);
                        Assert.Equal(baseWidths[4], list.Columns[4].Width);
                        Assert.Equal(new[] { Strings.ColumnAddress, Strings.ColumnCompany, Strings.ColumnWhatItIs, Strings.ColumnLevel, Strings.ColumnStatus },
                            list.Columns.Cast<ColumnHeader>().Select(c => c.Text));
                        Assert.Equal(4, FixDialog.StatusColumn);
                        d.Close();
                    }
                    // Where the body doesn't scroll at the smallest size, the minimums fit and the columns fill the list exactly.
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Sta.ShowOffScreen(d);
                        d.Size = d.MinimumSize;
                        Application.DoEvents();
                        Assert.False(d.ContentScrolls);
                        ThemedListView list = d.PreviewList;
                        (int[] baseWidths, int[] minWidths) = d.PreviewColumnWidths();
                        Assert.True(list.ClientSize.Width >= minWidths.Sum(), "the minimum columns don't fit at the smallest size");
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));
                        Assert.True(list.Columns[1].Width < baseWidths[1], "Company didn't shrink first");
                        // The columns fit, so no horizontal scroll bar takes a row's room (the list once kept the one it showed
                        // while it was narrowed): the header and four whole rows show.
                        Assert.False(ScrollsSideways(list), "the columns fit, but the list shows a horizontal scroll bar");
                        Assert.True(list.ClientSize.Height >= list.HeaderHeight + 4 * list.RowHeight, "fewer than four rows show: " + list.ClientSize.Height);
                        // Wider and back: still no bar.
                        d.Width += UiScale.Current.Px(40);
                        Application.DoEvents();
                        d.Size = d.MinimumSize;
                        Application.DoEvents();
                        Assert.False(ScrollsSideways(list), "the bar came back after a resize");
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-05: the cells (5.6)

        [Fact]
        public void The_cells_show_pills_by_status_and_the_effects_glyph_by_note()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        ThemeManager.Override(Theme.Light);
                        Theme t = Theme.Light;
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3))))
                        {
                            Sta.ShowOffScreen(d);
                            ThemedListView list = d.PreviewList;
                            UiScale s = UiScale.Current;
                            // Rows of the first screenful (allowed rows come first, then A to Z).
                            List<ListViewItem> top = list.Items.Cast<ListViewItem>().Take(10).ToList();
                            ListViewItem allowed = top.First(i => ((FixRow)i.Tag).Allowed);
                            ListViewItem strictNote = top.First(i => !((FixRow)i.Tag).Allowed && ((FixRow)i.Tag).Tier == Tier.Strict && ((FixRow)i.Tag).Note.Length > 0);
                            ListViewItem plain = top.First(i => !((FixRow)i.Tag).Allowed && ((FixRow)i.Tag).Tier == Tier.Default && ((FixRow)i.Tag).Note.Length == 0);
                            // The sub-items stay the words screen readers and tests read.
                            Assert.Equal(new[] { plain.Text, ((FixRow)plain.Tag).Company, Strings.CategoryWords(((FixRow)plain.Tag).Entry.Category), Strings.TierName(Tier.Default), Strings.StatusBlocked },
                                plain.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text));
                            Assert.Equal(Strings.StatusAllowed, allowed.SubItems[FixDialog.StatusColumn].Text);
                            Assert.Equal(Strings.FixRowToolTip(strictNote.Text, ((FixRow)strictNote.Tag).Note), strictNote.ToolTipText);
                            Application.DoEvents();
                            using (Bitmap bmp = Pixels.Of(list))
                            {
                                Rectangle CellOf(ListViewItem item, int column) => list.CellBounds(item.Bounds, column);
                                // A pill's fill just right of its left end, on the row's middle line (no text there).
                                int PillFill(ListViewItem item, int column)
                                {
                                    Rectangle cell = CellOf(item, column);
                                    return Pixels.Rgb(bmp, cell.X + s.Px(12) + s.Px(5), cell.Y + cell.Height / 2 - s.Px(4));
                                }
                                Assert.Equal(Pixels.Rgb(t.PillNeutralFill), PillFill(plain, FixDialog.StatusColumn));
                                Assert.Equal(Pixels.Rgb(t.PillAccentFill), PillFill(allowed, FixDialog.StatusColumn));
                                // The Strict pill is an outline: its left edge has the outline color; Standard rows have nothing there.
                                bool OutlineAt(ListViewItem item)
                                {
                                    Rectangle cell = CellOf(item, 3);
                                    int y = cell.Y + cell.Height / 2;
                                    for (int x = cell.X + s.Px(12); x < cell.X + s.Px(12) + s.Px(6); x++)
                                    {
                                        if (Pixels.Rgb(bmp, x, y) != Pixels.Rgb(t.CardBackground)) return true;
                                    }
                                    return false;
                                }
                                Assert.True(OutlineAt(strictNote), "no Strict pill on a Strict row");
                                Assert.False(OutlineAt(plain), "a Strict pill on a Standard row");
                                // The info glyph: 16 epx, 8 epx from the Address cell's right edge, only on a row with a note.
                                bool InkInGlyphBox(ListViewItem item)
                                {
                                    Rectangle cell = CellOf(item, 0);
                                    int px = s.Px(16);
                                    var box = new Rectangle(cell.Right - s.Px(8) - px, cell.Y + (cell.Height - px) / 2, px, px);
                                    for (int x = box.Left; x < box.Right; x++)
                                        for (int y = box.Top; y < box.Bottom; y++)
                                            if (Pixels.Rgb(bmp, x, y) != Pixels.Rgb(t.CardBackground)) return true;
                                    return false;
                                }
                                Assert.True(InkInGlyphBox(strictNote), "no effects glyph on a row with a note");
                                Assert.False(InkInGlyphBox(plain), "an effects glyph on a row without a note");
                            }
                            d.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-06: the details line (5.7)

        [Fact]
        public void The_details_line_shows_the_known_effects_of_the_focused_row()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Sta.ShowOffScreen(d);
                        ThemedLabel details = d.PreviewDetails;
                        Assert.Equal(AutomationLiveSetting.Polite, details.LiveSetting);
                        Assert.Equal(Glyph.Warning, details.LeadingGlyph);
                        Assert.True(details.Height >= 2 * d.PreviewFooter.Font.Height);
                        // Rows and no selection: the hint, in secondary text, no glyph.
                        Assert.Equal(Strings.FixNoSelection, details.Text);
                        Assert.True(details.LeadingGlyphHidden);
                        Assert.Equal(TextRole.Secondary, details.Role);
                        int textX = details.Padding.Left;

                        ListEntry note = p.Lists.DefaultEntries.First(e => e.Note.Length > 0);
                        d.PreviewSearch(note.Host);
                        d.PreviewSelectHost(note.Host);
                        Assert.Equal(Strings.FixRowDetails(note.Host, note.Company, Strings.TierName(note.Tier), note.Note), details.Text);
                        Assert.False(details.LeadingGlyphHidden);
                        Assert.Equal(TextRole.Primary, details.Role);

                        // No note: "None known" and no glyph, but its room stays (the text doesn't jump).
                        string plain = UiScenarios.NoNoteHost(p.Lists);
                        d.PreviewSearch(plain);
                        d.PreviewSelectHost(plain);
                        Assert.EndsWith(": " + Strings.NoneKnown, details.Text, StringComparison.Ordinal);
                        Assert.True(details.LeadingGlyphHidden);
                        Assert.Equal(textX, details.Padding.Left);

                        // No rows: empty.
                        d.PreviewSearch("example-shop");
                        Assert.Equal(string.Empty, details.Text);
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-07: the empty state (5.8)

        [Fact]
        public void A_search_with_no_match_offers_the_last_step()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var d = new FixDialog(c))
                    {
                        Sta.ShowOffScreen(d);
                        EmptyStatePanel empty = d.PreviewEmptyState;
                        Assert.False(Shows(d, empty));
                        d.PreviewSearch("example-shop");
                        Assert.True(Shows(d, empty));
                        Assert.Empty(d.PreviewList.Items);
                        Assert.Equal(Strings.FixNoMatch("example-shop"), empty.Message.Text);
                        Assert.Equal(Strings.FixNoMatchHint, empty.Hint.Text);
                        Assert.True(Shows(d, empty.GoToStep));
                        Assert.Equal(Strings.FixGoToStep(2), empty.GoToStep.Text);
                        // It covers the list below the header (the list stays, with no items, for screen readers).
                        Assert.Equal(d.PreviewList.Top + d.PreviewList.HeaderHeight, empty.Top);
                        // The hidden empty list takes no Tab (it would show no focus, a11y-6): Tab from the search box goes on
                        // to "Go to step 2". The list comes back into the tab order with its rows.
                        Assert.False(d.PreviewList.TabStop);
                        d.PreviewSearchBox.TextBox.Focus();
                        Flow.SendKey(d, Keys.Tab);
                        Assert.True(empty.GoToStep.Focused, "Tab after a search with no match didn't reach Go to step 2");
                        empty.GoToStep.PerformClick();
                        Assert.True(d.PreviewLastButton.Focused, "Go to step 2 didn't move the focus to the step's button");
                        // While a change runs the step's button waits, and so the link goes.
                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.False(Shows(d, empty.GoToStep));
                        c.PreviewClearBusy();
                        Assert.True(Shows(d, empty.GoToStep));
                        d.PreviewSearch(string.Empty);
                        Assert.False(Shows(d, empty));
                        Assert.True(d.PreviewList.TabStop);
                        d.Close();
                    }
                    // Strict: step 3. Can't read: the last card has no button, so no link.
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict))))
                    {
                        Sta.ShowOffScreen(d);
                        d.PreviewSearch("example-shop");
                        Assert.Equal(Strings.FixGoToStep(3), d.PreviewEmptyState.GoToStep.Text);
                        d.Close();
                    }
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.CantRead))))
                    {
                        Sta.ShowOffScreen(d);
                        d.PreviewSearch("example-shop");
                        Assert.True(Shows(d, d.PreviewEmptyState));
                        Assert.False(Shows(d, d.PreviewEmptyState.GoToStep));
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-08: the search (5.4)

        [Fact]
        public void The_search_waits_for_typing_and_answers_its_keys()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Sta.ShowOffScreen(d);
                        HintTextBox box = d.PreviewSearchBox.TextBox;
                        Assert.Same(box, d.ActiveControl);
                        Assert.Equal(200, d.PreviewSearchDelay);
                        int all = d.TotalRows;

                        // Typing: applied 200 ms after the last key, not before.
                        box.Text = "yahoo";
                        Assert.Equal(all, d.ShownRows);
                        Assert.True(d.PreviewSearchPending);
                        Flow.PumpUntil(() => d.ShownRows != all, 2000);
                        Assert.InRange(d.ShownRows, 1, all - 1);
                        Assert.False(d.PreviewSearchPending);

                        // Enter applies at once.
                        box.Text = "doubleclick";
                        d.PreviewSearchKey(Keys.Enter);
                        Assert.False(d.PreviewSearchPending);
                        Assert.Equal(FixList.Search(FixList.Build(p.Lists, Level.Standard, null), "doubleclick").Count, d.ShownRows);

                        // The clear button empties the box, applies at once and gives the box the focus.
                        d.PreviewList.Focus();
                        d.PreviewSearchBox.ClearButton.PerformClick();
                        Assert.Equal(string.Empty, box.Text);
                        Assert.Equal(all, d.ShownRows);
                        Assert.False(d.PreviewSearchPending);
                        Assert.True(box.Focused);

                        // Ctrl+F from anywhere focuses the box and selects its text.
                        box.Text = "ads";
                        d.PreviewSearchKey(Keys.Enter);
                        d.PreviewList.Focus();
                        Flow.SendKey(d, Keys.Control | Keys.F);
                        Assert.True(box.Focused);
                        Assert.Equal(box.TextLength, box.SelectionLength);

                        // Down enters the list at its first row.
                        d.PreviewSearchKey(Keys.Down);
                        Assert.True(d.PreviewList.Focused);
                        Assert.True(d.PreviewList.Items[0].Selected);
                        Assert.True(d.PreviewList.Items[0].Focused);

                        // Esc closes (Close is the Cancel button, so also the title-bar X).
                        Assert.Same(d.PreviewClose, d.CancelButton);
                        Assert.Equal(DialogResult.Cancel, d.PreviewClose.DialogResult);
                        Assert.True(Flow.Enter(d, Keys.Escape));
                        Assert.Equal(DialogResult.Cancel, d.DialogResult);
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-09: Allow and Block again (5.9)

        [Fact]
        public void Allow_and_block_again_follow_the_selection_and_the_state()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3));
                    using (var d = new FixDialog(c))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(Strings.FixConsequence + " " + Strings.ShieldDescription, d.PreviewAllow.AccessibleDescription);
                        Assert.Equal(Strings.ShieldDescription, d.PreviewBlockAgain.AccessibleDescription);
                        Assert.Equal(ButtonStyle.Accent, d.PreviewAllow.Style);
                        Assert.Equal(ButtonStyle.Standard, d.PreviewBlockAgain.Style);
                        Assert.False(d.CanAllow);
                        Assert.False(d.CanBlockAgain);
                        // Allowed rows only: Block again.
                        d.PreviewSelectAllowed(2);
                        Assert.False(d.CanAllow);
                        Assert.True(d.CanBlockAgain);
                        // Allowed and blocked rows together: both.
                        d.PreviewSelect(5);
                        Assert.True(d.CanAllow);
                        Assert.True(d.CanBlockAgain);
                        // More than 100: neither.
                        d.PreviewSelect(Limits.MaxHostsPerRun + 1);
                        Assert.False(d.CanAllow);
                        Assert.False(d.CanBlockAgain);
                        // Exactly 100: fine.
                        d.PreviewSearch(string.Empty);
                        d.PreviewSelect(Limits.MaxHostsPerRun);
                        Assert.True(d.CanAllow);
                        // While a change runs: neither.
                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.False(d.CanAllow);
                        Assert.False(d.CanBlockAgain);
                        c.PreviewClearBusy();
                        Assert.True(d.CanAllow);
                        d.Close();
                    }
                    // Not on, or another run holds the mutex: neither.
                    foreach (HostsStatus s in new[]
                    {
                        UiScenarios.Status(p.Lists, ProtectionState.Off, allowed: 2),
                        UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3, runInProgress: true),
                    })
                    {
                        using (var d = new FixDialog(new AppController(p.Context, s)))
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewSelect(4);
                            Assert.False(d.CanAllow);
                            Assert.False(d.CanBlockAgain);
                            d.Close();
                        }
                    }
                });
            }
        }

        [Fact]
        public void Allow_and_block_again_run_the_selected_addresses_of_their_kind()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var commands = new List<ElevatedCommand>();
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3);
                    var c = new AppController(p.Context, s, Flow.Returns(ResultKind.Declined, commands), () => s);
                    using (var d = new FixDialog(c))
                    {
                        Sta.ShowOffScreen(d);
                        d.PreviewSelect(5);
                        List<string> selected = d.PreviewList.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
                        Flow.Click(d, d.PreviewAllow);
                        Flow.PumpUntil(() => commands.Count == 1 && !c.IsBusy);
                        Assert.Equal(Verb.Allow, commands[0].Verb);
                        Assert.Equal(selected.Skip(3), commands[0].Hosts);
                        d.PreviewSelect(5);
                        Flow.Click(d, d.PreviewBlockAgain);
                        Flow.PumpUntil(() => commands.Count == 2 && !c.IsBusy);
                        Assert.Equal(Verb.Block, commands[1].Verb);
                        Assert.Equal(s.Allowlist, commands[1].Hosts);
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-10: Enter (K2)

        [Fact]
        public void Enter_never_runs_a_step_but_still_clicks_allow_and_does_nothing_in_the_list()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (string state in new[] { "on-strict", "off", "off-after-test", "removed", "older-list" })
                    {
                        int launches = 0;
                        AppController c = ControllerFor(p, state, (cmd, progress, owner) =>
                        {
                            launches++;
                            return Task.FromResult(new LaunchResult(ResultKind.Declined, null, null, Flow.Hash, Flow.Hash));
                        });
                        using (var d = new FixDialog(c))
                        {
                            Sta.ShowOffScreen(d);
                            Assert.Null(d.AcceptButton);
                            var buttons = new List<ThemedButton> { d.PreviewLastButton };
                            if (state == "on-strict") buttons.Add(d.PreviewSwitchToStandard);
                            foreach (ThemedButton b in buttons)
                            {
                                int clicks = 0;
                                b.Click += (o, e) => clicks++;
                                d.ActiveControl = b;
                                foreach (Keys keys in new[] { Keys.Enter, Keys.Enter | Keys.Shift })
                                {
                                    Assert.True(Flow.Enter(d, keys), keys + " wasn't swallowed on " + b.Text);
                                    Assert.True(clicks == 0, keys + " clicked " + b.Text);
                                }
                            }
                            // Enter in the list: nothing (no default button, no item activation). Rows 0-2 are allowed on Strict.
                            d.PreviewSelect(4);
                            d.ActiveControl = d.PreviewList;
                            Flow.Enter(d, Keys.Enter);
                            Flow.PumpFor(150);
                            Assert.Equal(0, launches);
                            if (state == "on-strict")
                            {
                                // Allow takes Enter as a focused button does.
                                int allows = 0;
                                d.PreviewAllow.Click += (o, e) => allows++;
                                Assert.True(d.PreviewAllow.Enabled);
                                d.ActiveControl = d.PreviewAllow;
                                Flow.Enter(d, Keys.Enter);
                                Assert.Equal(1, allows);
                            }
                            d.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-11: the result bar inside the dialog (5.10)

        [Fact]
        public void A_result_that_arrives_while_open_shows_until_the_next_change()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3);
                    var c = new AppController(p.Context, s, Flow.Returns(ResultKind.Declined), () => s);
                    int finished = 0;
                    c.ChangeFinished += (o, e) => finished++;
                    // A result from before the dialog opened doesn't show here.
                    c.PreviewResult(Strings.ResultTurnOnOk, null);
                    finished = 0;
                    using (var d = new FixDialog(c) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(d);
                        NoticeBar bar = d.PreviewResultBar;
                        Assert.False(Shows(d, bar));
                        // A change from the dialog: its result shows here, closable, without an action.
                        d.PreviewSelect(4);
                        Flow.Click(d, d.PreviewAllow);
                        Flow.PumpUntil(() => finished == 1 && !c.IsBusy);
                        Assert.True(Shows(d, bar));
                        Assert.Equal(Strings.ResultDeclined, bar.Message);
                        Assert.Equal(NoticeSeverity.Info, bar.Severity);
                        Assert.True(bar.Closable);
                        Assert.Null(bar.Action);
                        Assert.Equal(Strings.PrefixNote + Strings.ResultDeclined, bar.AccessibleName);
                        // The next change starts: it goes.
                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.False(Shows(d, bar));
                        c.PreviewClearBusy();
                        Assert.False(Shows(d, bar));

                        // A result again, then its close button: gone in both windows, and the focus moves to Allow (or the list).
                        c.PreviewResult(Result(p, ChangeKind.Allow, ResultKind.Success), d);
                        Assert.True(Shows(d, bar));
                        Assert.Equal(NoticeSeverity.Success, bar.Severity);
                        bar.CloseButton!.Focus();
                        bar.CloseButton.PerformClick();
                        Assert.Null(c.LastResult);
                        Assert.False(Shows(d, bar));
                        Assert.True(d.PreviewAllow.Focused || d.PreviewList.Focused, "the focus didn't move to Allow or the list");
                        d.Close();
                    }
                });
            }
        }

        private static ChangeResult Result(PreviewContext p, ChangeKind kind, ResultKind result) =>
            UiScenarios.Result(p.Lists, kind, ElevatedCommand.Allow(new[] { p.Lists.DefaultEntries.First().Host }), result, UiScenarios.Status(p.Lists, ProtectionState.On));

        // ---------------------------------------------------------------- T-C-12: the search box's description (5.11)

        [Fact]
        public void The_search_box_names_the_steps_shown()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    string hint = " " + Strings.FixSearchHint;
                    foreach ((string state, string[] steps, bool canAllow) in new (string, string[], bool)[]
                    {
                        ("on-strict", new[] { Strings.FixStepShortRestart, Strings.FixStepShortStandard, Strings.FixStepShortTest }, true),
                        ("on-standard", new[] { Strings.FixStepShortRestart, Strings.FixStepShortTest }, true),
                        ("off", new[] { Strings.FixStepShortRestart, Strings.FixStepShortTurnOn }, true),
                        ("off-after-test", new[] { Strings.FixStepShortRestart, Strings.FixStepShortTurnOn }, true),
                        ("removed", new[] { Strings.FixStepShortRestart, Strings.FixStepShortTurnOn }, true),
                        ("older-list", new[] { Strings.FixStepShortRestart, Strings.FixStepShortOlderList }, true),
                        ("cant-read", new[] { Strings.FixStepShortRestart }, false),
                    })
                    {
                        using (var d = new FixDialog(ControllerFor(p, state)))
                        {
                            HintTextBox box = d.PreviewSearchBox.TextBox;
                            Assert.Equal(Strings.FixSearchAccessibleName, box.AccessibleName);
                            Assert.Equal(Strings.FixDescription(steps, canAllow) + hint, box.AccessibleDescription);
                        }
                    }
                    using (var d = new FixDialog(ControllerFor(p, "on-strict")))
                    {
                        Assert.Equal("Steps: restart your browser; try Standard; test with protection off. Then search for an address to allow. " +
                            "Site, company or address, for example yahoo", d.PreviewSearchBox.TextBox.AccessibleDescription);
                        Assert.Equal(AccessibleRole.Dialog, d.AccessibleRole);
                        Assert.Equal(Strings.FixTitle, d.Text);
                        Assert.Equal(Strings.FixListAccessibleName, d.PreviewList.AccessibleName);
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-13: the site report (5.12)

        [Fact]
        public void Copy_details_copies_the_site_report_without_any_address()
        {
            using (var p = new PreviewContext())
            {
                HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3, warnings: HostsWarnings.LargeFile);
                string report = DetailsReport.BuildSiteReport(p.Context, s);
                string[] lines = report.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(new[] { Strings.DetailsApp, Strings.DetailsLists, Strings.DetailsWindows, Strings.DetailsState, Strings.DetailsLevel, Strings.DetailsAllowedCount, Strings.DetailsTextSize },
                    lines.Select(l => l.Substring(0, l.IndexOf(": ", StringComparison.Ordinal))));
                Assert.Contains(Strings.DetailsApp + ": QuietHosts " + AppInfo.Version, lines);
                Assert.Contains(Strings.DetailsLists + ": " + p.Lists.Version, lines);
                Assert.Contains(Strings.DetailsState + ": On, LargeFile", lines);
                Assert.Contains(Strings.DetailsLevel + ": Strict", lines);
                Assert.Contains(Strings.DetailsAllowedCount + ": 3", lines);
                // Never an address's name, never the last run.
                foreach (string host in s.Allowlist.Concat(s.BlockedHosts.Take(50))) Assert.DoesNotContain(host, report);
                Assert.DoesNotContain(Strings.DetailsLastRun, report);
                Assert.DoesNotContain(UiScenarios.FakeLastRun.Split('\n')[0], report);
                Assert.Equal(Diagnostics.MaskProfilePath(report), report);

                var heard = new List<(string Text, Announcement Kind)>();
                Sta.Run(() =>
                {
                    global::QuietHosts.Ui.AnnouncementSink = (c, text, kind) => heard.Add((text, kind));
                    try
                    {
                        string? copied = null;
                        using (var d = new FixDialog(new AppController(p.Context, s)) { PreviewClipboard = text => { copied = text; return true; } })
                        {
                            Sta.ShowOffScreen(d);
                            // At the default size the prompt and both links share one line.
                            Assert.Equal(d.PreviewReportLink.Top, d.PreviewCopyDetailsLink.Top);
                            Assert.True(d.PreviewCopyDetailsLink.Left > d.PreviewReportLink.Right);
                            Assert.False(Shows(d, d.PreviewCopyFeedback));
                            d.PreviewCopyDetailsLink.PerformClick();
                            Assert.Equal(DetailsReport.BuildSiteReport(p.Context, s), copied);
                            Assert.True(Shows(d, d.PreviewCopyFeedback));
                            Assert.Equal(Strings.DetailsCopied, d.PreviewCopyFeedback.Text);
                            Assert.Contains((Strings.DetailsCopied, Announcement.Result), heard);
                            // The report link opens the broken-site form (external).
                            string? opened = null;
                            d.PreviewOpenUrl = url => opened = url;
                            d.PreviewReportLink.PerformClick();
                            Assert.Equal(Strings.ReportSiteUrl, opened);
                            Assert.True(d.PreviewReportLink.External);
                            d.Close();
                        }
                        using (var d = new FixDialog(new AppController(p.Context, s)) { PreviewClipboard = text => false })
                        {
                            Sta.ShowOffScreen(d);
                            d.PreviewCopyDetailsLink.PerformClick();
                            Assert.Equal(Strings.DetailsNotCopied, d.PreviewCopyFeedback.Text);
                            d.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-21: large text and short screens (D18)

        [Fact]
        public void On_a_short_screen_the_list_keeps_four_rows_and_the_body_scrolls()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        var cases = new List<(Rectangle Area, float Text)> { (UiScenarios.Scaled(UiScenarios.ShortScreen), 1f) };
#if DEBUG
                        cases.Add((UiScenarios.Scaled(UiScenarios.LaptopScreen), 1.5f));
#endif
                        foreach ((Rectangle area, float text) in cases)
                        {
#if DEBUG
                            UiFonts.TextScale = text;
#endif
                            using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict))) { PreviewWorkingArea = area })
                            {
                                Sta.ShowOffScreen(d);
                                string what = area.Height + " px screen at " + text + "x text: ";
                                Assert.True(d.Height <= area.Height, what + "the dialog is " + d.Height + " px tall");
                                Assert.True(d.ContentScrolls, what + "the body doesn't scroll");
                                Assert.True(d.PreviewListCard.Height >= d.PreviewMinListCardHeight, what + "the list has fewer than four rows");
                                foreach (Control button in new Control[] { d.PreviewAllow, d.PreviewBlockAgain, d.PreviewClose })
                                {
                                    Rectangle r = d.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                                    Assert.True(r.Top >= 0 && r.Bottom <= d.ClientSize.Height, what + button.Text + " is outside the window");
                                }
                                // Tab from the search box (the first focus, scrolled into view) to the list scrolls the list into view.
                                int before = d.PreviewHost.ScrollTop;
                                d.PreviewSearchBox.TextBox.Focus();
                                Flow.SendKey(d, Keys.Tab);
                                Assert.True(d.PreviewList.Focused, what + "Tab didn't reach the list");
                                ScrollHost host = d.PreviewHost;
                                int top = host.PointToClient(d.PreviewList.PointToScreen(Point.Empty)).Y;
                                Assert.True(top >= 0 && top < host.ClientSize.Height, what + "the list isn't scrolled into view (top " + top + ")");
                                Assert.True(host.ScrollTop > before);
                                d.Close();
                            }
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
        public void At_the_largest_text_size_the_step_cards_stack_and_the_filter_goes_below_the_search_box()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        UiScale s = UiScale.Current;
                        UiFonts.TextScale = 2.25f;
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict))))
                        {
                            Sta.ShowOffScreen(d);
                            Assert.True(d.PreviewStepRow.IsStacked, "the step cards don't stack at 225% text");
                            IReadOnlyList<StepCard> cards = d.PreviewStepCards;
                            // One per row, full width, 8 epx apart.
                            Assert.Equal(cards[0].Width, cards[1].Width);
                            Assert.Equal(d.PreviewStepRow.Width, cards[2].Width);
                            Assert.Equal(cards[0].Bottom + s.Px(8), cards[1].Top);
                            // Beside the filter the search box would be too narrow: the filter goes 8 epx below the full-width
                            // box, its box in line with the search box, and never past the body's padding.
                            ThemedCheckBox filter = d.PreviewFilter;
                            Assert.Equal(d.PreviewStepRow.Width, d.PreviewSearchBox.Width);
                            Assert.Equal(d.PreviewSearchBox.Bottom + s.Px(8), filter.Top);
                            Assert.Equal(d.PreviewSearchBox.Left, filter.Left + filter.BoxBounds.Left);
                            Assert.True(filter.Right <= d.PreviewStepRow.Right + s.Px(ThemedCheckBox.RingRoom), "the filter runs past the body");
                            // At the smallest size even the minimum columns are wider than the list: the list makes room for its
                            // horizontal scroll bar, so four whole rows still show (D18).
                            d.Size = d.MinimumSize;
                            Application.DoEvents();
                            ThemedListView list = d.PreviewList;
                            Assert.True(list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) > list.ClientSize.Width, "the columns fit; this case needs a narrower list");
                            Assert.True(ScrollsSideways(list));
                            Assert.True(list.ClientSize.Height >= list.HeaderHeight + 4 * list.RowHeight, "fewer than four rows show: " + list.ClientSize.Height);
                            d.Close();
                        }
                        UiFonts.TextScale = 1f;
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict))))
                        {
                            Sta.ShowOffScreen(d);
                            Assert.False(d.PreviewStepRow.IsStacked);
                            IReadOnlyList<StepCard> cards = d.PreviewStepCards;
                            // Side by side: equally wide (the last takes the leftover pixels), equally tall, the buttons on one line.
                            Assert.InRange(cards[2].Width - cards[0].Width, 0, 2);
                            Assert.Equal(cards[0].Height, cards[2].Height);
                            Assert.Equal(d.PreviewSwitchToStandard.Bottom, d.PreviewLastButton.Bottom);
                            // The filter sits beside the search box, 16 epx from it to its box.
                            ThemedCheckBox filter = d.PreviewFilter;
                            Assert.Equal(d.PreviewSearchBox.Right + s.Px(16), filter.Left + filter.BoxBounds.Left);
                            Assert.Equal(d.PreviewStepRow.Right, filter.Right);
                            d.Close();
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

        [Fact]
        public void The_dialog_opens_at_its_default_size_in_every_state_that_fits()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiScale s = UiScale.Current;
                    foreach (string state in new[] { "on-standard", "on-strict", "off", "cant-read" })
                    {
                        using (var d = new FixDialog(ControllerFor(p, state)))
                        {
                            Sta.ShowOffScreen(d);
                            // 760 x 680 epx (taller only by what four list rows need), and the body fits without scrolling: the
                            // minimum is measured where the step cards don't stack, so Strict's three cards don't make it 880 tall.
                            Assert.Equal(s.Px(760), d.ClientSize.Width);
                            Assert.InRange(d.ClientSize.Height, s.Px(680), s.Px(700));
                            Assert.False(d.ContentScrolls, state + ": the body scrolls at the default size");
                            Assert.True(d.MinimumSize.Height <= d.Height, state + ": the smallest size is taller than the default");
                            d.Close();
                        }
                    }
                });
            }
        }
    }
}
