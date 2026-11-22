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
    /// <summary>The first run (spec 4): its text by signature state, its two buttons, Esc and what Narrator reads.</summary>
    [Collection(WindowCollection.Name)]
    public class FirstRunTests
    {
        /// <summary>The signature states (as their names: the enum is internal).</summary>
        public static IEnumerable<object[]> Signatures() => new[]
        {
            new object[] { nameof(SignatureKind.Unsigned), null! },
            new object[] { nameof(SignatureKind.Unverified), null! },
            new object[] { nameof(SignatureKind.Signed), "Example Publisher" },
        };

        // ---------------------------------------------------------------- T-B-18

        [Theory]
        [MemberData(nameof(Signatures))]
        public void The_first_run_names_the_publisher_only_when_signed(string signature, string? publisher)
        {
            var kind = (SignatureKind)Enum.Parse(typeof(SignatureKind), signature);
            using (var p = new PreviewContext())
            {
                var ctx = new UiContext(p.Env, p.Lists, p.Context.ExePath, RunLocationTips.None, false, true, new SignatureState(kind, publisher));
                Sta.Run(() =>
                {
                    using (var f = new MainForm(new AppController(ctx, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false)), showFirstRun: true))
                    {
                        Assert.True(f.IsShowingFirstRun);
                        List<string> shown = Flow.ShownTexts(f).ToList();
                        string fact2 = kind == SignatureKind.Signed ? Strings.FirstRunFact2Signed(publisher!) : Strings.FirstRunFact2Unsigned;
                        foreach (string text in new[]
                        {
                            Strings.FirstRunTitle, Strings.FirstRunFact1Title, Strings.FirstRunFact1Body(p.Lists.StandardCount), Strings.FirstRunFact2Title, fact2,
                            Strings.FirstRunFact3Title, Strings.FirstRunFact3Body, Strings.FirstRunLimit, Strings.FirstRunWhyLink, Strings.FirstRunTrust,
                        })
                            Assert.Contains(text, shown);
                        Assert.DoesNotContain(Strings.FirstRunLegacy, shown);
                        Assert.DoesNotContain(Strings.LevelHeading, shown);
                        if (kind == SignatureKind.Signed) Assert.Equal("Windows shows \u201CExample Publisher\u201D as the publisher. Say yes only if the prompt names QuietHosts and you just clicked a QuietHosts button.", fact2);
                        else Assert.Contains("\u201CUnknown publisher\u201D", fact2);
                    }
                });
            }
        }

        [Fact]
        public void The_first_run_text_and_its_buttons_follow_the_spec()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiScale s = UiScale.Current;
                    HostsStatus older = UiScenarios.Status(p.Lists, ProtectionState.OlderList, firstRunDone: false);
                    using (var f = new MainForm(new AppController(p.Context, older), showFirstRun: true))
                    {
                        Sta.ShowOffScreen(f);
                        FirstRunPanel run = f.PreviewFirstRun!;
                        Assert.True(ControlTree.IsMeantToShow(run.Legacy!, f));
                        Assert.Equal(Strings.FirstRunLegacy, run.Legacy!.Text);
                        Assert.Equal(s.Px(Metrics.AppIcon), run.Icon.Width);
                        Assert.Equal(TextStyle.Title, run.Title.Style);
                        // Each fact's body lines up with its title's text, after the 20 epx glyph and 12 epx gap.
                        Assert.All(run.FactBodies, b => Assert.Equal(s.Px(20) + s.Px(12), b.Left - run.Title.Left));
                        Assert.All(run.FactBodies, b => Assert.Equal(TextRole.Secondary, b.Role));

                        ThemedButton turnOn = run.TurnOnButton;
                        Assert.Equal(ButtonStyle.Accent, turnOn.Style);
                        Assert.True(turnOn.Shield);
                        Assert.True(turnOn.SwallowsEnter);
                        Assert.Equal(ButtonStyle.Standard, run.NotNowButton.Style);
                        Assert.Same(run.NotNowButton, f.CancelButton);
                        Assert.Null(f.AcceptButton);
                        // The first focus: Turn on protection.
                        Assert.True(turnOn.Focused);

                        // Narrator reads the whole text with the focused button: the three facts, the older list, the limit, the trust line.
                        string spoken = turnOn.AccessibleDescription;
                        Assert.Equal(run.SpokenText + " " + Strings.ShieldDescription, spoken);
                        Assert.StartsWith(Strings.FirstRunTitle + ". " + Strings.FirstRunFact1Title + ". " + Strings.FirstRunFact1Body(p.Lists.StandardCount), spoken, StringComparison.Ordinal);
                        foreach (string text in new[] { Strings.FirstRunFact2Title + ". " + Strings.FirstRunFact2Unsigned, Strings.FirstRunFact3Title + ". " + Strings.FirstRunFact3Body,
                            Strings.FirstRunLegacy, Strings.FirstRunLimit, Strings.FirstRunTrust })
                            Assert.Contains(text, spoken);
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void The_first_runs_text_starts_on_the_edge_of_its_icon_glyphs_and_buttons()
        {
            // The title started 9 px right of the fact glyphs and of Turn on protection, and the other lines GDI's 4 px margin
            // right of them (visual-5): every text is tight and starts on the 24 epx edge.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        ThemeManager.Override(Theme.Light);
                        UiScale s = UiScale.Current;
                        HostsStatus older = UiScenarios.Status(p.Lists, ProtectionState.OlderList, firstRunDone: false);
                        using (var f = new MainForm(new AppController(p.Context, older), showFirstRun: true))
                        {
                            Sta.ShowOffScreen(f);
                            FirstRunPanel run = f.PreviewFirstRun!;
                            int X(Control c) => f.PointToClient(c.PointToScreen(System.Drawing.Point.Empty)).X;
                            int edge = s.Px(Metrics.PagePadding);
                            Assert.Equal(edge, X(run.Icon));
                            Assert.Equal(edge, X(run.TurnOnButton));
                            foreach (ThemedLabel label in new[] { run.Title, run.Legacy!, run.Limit, run.Trust })
                            {
                                Assert.True(label.Tight, label.Text);
                                Assert.Equal(edge, X(label));
                            }
                            // The fact bodies line up with the fact titles' text, 12 epx after the 20 epx glyph.
                            Assert.All(run.FactBodies, b => Assert.Equal(edge + s.Px(32), X(b)));
                            // At the normal text size the limit sentence and "Why?" share one line, "Why?" right after the sentence.
                            Assert.True(run.Limit.Height < 2 * run.Limit.Font.Height, "the limit sentence wraps at 100% text: " + run.Limit.Size);
                            Assert.Equal(run.WhyLink.Top + run.WhyLink.Padding.Top, run.Limit.Top);
                            Assert.Equal(run.Limit.Right, run.WhyLink.Left);
                            using (System.Drawing.Bitmap bmp = Pixels.Of(run))
                            {
                                foreach (ThemedLabel label in new[] { run.Title, run.Legacy!, run.Trust })
                                {
                                    int first = MainWindowTests.FirstInk(bmp, label.Bounds, Theme.Light.WindowBackground);
                                    Assert.InRange(first + X(run) - edge, 0, s.Px(3));
                                }
                            }
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
        public void At_large_text_why_follows_the_limit_sentence_or_starts_a_line_on_its_edge()
        {
            // At 150% text "Why?" went below a one-word last line, 4 px right of the text (visual-9): it follows the sentence's
            // last line when it fits there, and otherwise starts the next line with its letters on the text's edge.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (float text in new[] { 1f, 1.5f, 1.75f, 2.25f })
                        {
                            UiFonts.TextScale = text;
                            using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false)), showFirstRun: true))
                            {
                                Sta.ShowOffScreen(f);
                                FirstRunPanel run = f.PreviewFirstRun!;
                                ThemedLabel limit = run.Limit;
                                ThemedLink why = run.WhyLink;
                                string what = text + "x text: ";
                                // The whole sentence shows: it wraps in the line's width and is as tall as its lines (a label
                                // that sized itself took one line, and the line's edge cut "way." off).
                                Assert.True(limit.Right <= run.LimitLine.Width, what + "the sentence runs past the line");
                                Assert.Equal(limit.GetPreferredSize(new System.Drawing.Size(limit.Width, 0)).Height, limit.Height);
                                int lines = Math.Max(1, limit.Height / limit.Font.Height);
                                int lastLineTop = limit.Top + (lines - 1) * limit.Font.Height;
                                if (why.Top + why.Padding.Top >= limit.Bottom)
                                {
                                    // A line of its own: its letters on the sentence's edge.
                                    Assert.Equal(limit.Left + TextFlow.Lead(limit), why.Left + TextFlow.Lead(why));
                                }
                                else
                                {
                                    // On the sentence's last line, after its text: the same line, right of the sentence's letters.
                                    Assert.Equal(lastLineTop, why.Top + why.Padding.Top);
                                    Assert.True(why.Right <= run.LimitLine.Width, what + "Why? runs past the line");
                                    if (limit.Height >= 2 * limit.Font.Height) Assert.True(run.LimitLine.PreviewCuts(limit), what + "the wrapped sentence covers Why?");
                                }
                                f.Close();
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
#endif

        [Fact]
        public void Turn_on_protection_uses_standard_marks_the_first_run_done_and_focuses_the_main_button()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                BlockLists lists = BlockLists.LoadEmbedded();
                // Not a preview, so the first-run flag is stored (in this test environment's in-memory store).
                var ctx = new UiContext(env, lists, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                Sta.Run(() =>
                {
                    // Strict was chosen earlier: the first run's button still uses Standard.
                    HostsStatus off = UiScenarios.Status(lists, ProtectionState.Off, firstRunDone: false, pendingLevel: Level.Strict);
                    var commands = new List<ElevatedCommand>();
                    var c = new AppController(ctx, off, Flow.Returns(ResultKind.Declined, commands), () => off);
                    using (var f = new MainForm(c, showFirstRun: true) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        Flow.Click(f, f.PreviewFirstRun!.TurnOnButton);
                        Flow.PumpUntil(() => c.LastResult != null && !c.IsBusy);
                        Assert.False(f.IsShowingFirstRun);
                        Assert.True(env.User.Read().FirstRunDone);
                        ElevatedCommand cmd = Assert.Single(commands);
                        Assert.Equal(Verb.On, cmd.Verb);
                        Assert.Equal(Level.Standard, cmd.Level);
                        Assert.True(f.PreviewStatusCard.MainButton.Focused);
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void Not_now_and_esc_mark_the_first_run_done_and_show_the_page()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                BlockLists lists = BlockLists.LoadEmbedded();
                var ctx = new UiContext(env, lists, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                Sta.Run(() =>
                {
                    HostsStatus off = UiScenarios.Status(lists, ProtectionState.Off, firstRunDone: false);
                    foreach (bool esc in new[] { false, true })
                    {
                        env.User.SetFirstRunDone(false);
                        var commands = new List<ElevatedCommand>();
                        var c = new AppController(ctx, off, Flow.Returns(ResultKind.Success, commands), () => off);
                        using (var f = new MainForm(c, showFirstRun: true) { PreviewForeground = true })
                        {
                            Sta.ShowOffScreen(f);
                            if (esc) Assert.True(Flow.Enter(f, Keys.Escape));
                            else f.PreviewFirstRun!.NotNowButton.PerformClick();
                            Assert.False(f.IsShowingFirstRun);
                            Assert.True(env.User.Read().FirstRunDone);
                            Assert.Empty(commands);
                            Assert.True(f.PreviewStatusCard.MainButton.Focused);
                            Assert.True(ControlTree.IsMeantToShow(f.PreviewPage, f));
                            // Esc is Not now only while the first run shows.
                            Assert.Null(f.CancelButton);
                            Assert.False(Flow.Enter(f, Keys.Escape));
                            Assert.True(f.Visible);
                            f.Close();
                        }
                    }
                });
            }
        }

        [Fact]
        public void Why_opens_how_it_works_at_what_it_cant_block()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false)), showFirstRun: true))
                    using (var watch = new DialogWatch(d => DialogResult.Cancel))
                    {
                        watch.Describe = d => d is HowItWorksDialog how ? "how:" + how.Start : d.GetType().Name;
                        Sta.ShowOffScreen(f);
                        ThemedLink why = f.PreviewFirstRun!.WhyLink;
                        Assert.False(why.External);
                        why.PerformClick();
                        Assert.Equal(new[] { "how:" + HowItWorksSection.CantBlock }, watch.Answered);
                        Assert.True(f.IsShowingFirstRun);
                        f.Close();
                    }
                });
            }
        }
    }
}
