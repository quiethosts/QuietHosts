using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// Builds every window in every state with fake statuses on an STA thread (the same scenarios --render-ui
    /// draws), checks the access keys, and checks what the main window and the Fix dialog show. Nothing is
    /// shown on screen, started or saved.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class UiSmokeTests
    {
        public static IEnumerable<object[]> ScenarioNames()
        {
            using (var p = new PreviewContext())
            {
                return UiScenarios.All(p.Context).Select(s => new object[] { s.Name }).ToList();
            }
        }

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public void Each_window_builds_and_lays_out(string name)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiScenario scenario = UiScenarios.All(p.Context).Single(s => s.Name == name);
                    using (Form f = scenario.Create())
                    {
                        Assert.NotEqual(IntPtr.Zero, f.Handle);
                        f.PerformLayout();
                        scenario.AfterShown?.Invoke(f);
                        Assert.True(f.Width > 100 && f.Height > 100, name + " has no size");
                        Assert.False(string.IsNullOrWhiteSpace(f.Text), name + " has no title");
                        AssertAccessKeys(name, f);
                        AssertNoBorrowedNames(name, f);
                    }
                });
            }
        }

        /// <summary>
        /// No layout panel and no empty label borrows another control's text as its name: WinForms names an unnamed control
        /// after the label before it in the tab order, and Narrator read those names as context ("Selected, pane", a11y-5).
        /// A layout panel's name is empty, except a line of text and links, named by its own first sentence; a card or bar
        /// that is a group is named on purpose; an empty label's name is its empty text.
        /// </summary>
        private static void AssertNoBorrowedNames(string name, Form f)
        {
            foreach (Control c in ControlTree.All(f))
            {
                string? accessible = c.AccessibilityObject.Name;
                bool layout = (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel) && c.AccessibilityObject.Role != AccessibleRole.Grouping;
                if (c is TextFlow flow)
                {
                    Control first = flow.Parts[0];
                    Assert.Equal(first is LinkLabel ? string.Empty : first.Text, accessible);
                }
                else if (layout)
                    Assert.True(string.IsNullOrEmpty(accessible), name + ": the " + c.GetType().Name + " is named \"" + accessible + "\"");
                else if (c is Label && !(c is LinkLabel) && string.IsNullOrEmpty(c.Text))
                    Assert.True(string.IsNullOrEmpty(accessible), name + ": an empty " + c.GetType().Name + " is named \"" + accessible + "\"");
            }
        }

        /// <summary>
        /// Every button, option and link shown has its own access key, except the Cancel/Close button (Esc) and glyph-only
        /// buttons (no text, named for screen readers: a notice's close, clear search). The gallery shows each component in
        /// many states side by side; it is no window of the app, so its keys aren't unique by design.
        /// </summary>
        private static void AssertAccessKeys(string name, Form f)
        {
            if (f is GalleryForm) return;
            var seen = new Dictionary<char, string>();
            foreach (Control c in ControlTree.All(f))
            {
                if (!ControlTree.IsMeantToShow(c, f)) continue;
                bool takesKey = c is ButtonBase || c is LinkLabel || (c is Label label && label.UseMnemonic);
                if (!takesKey) continue;
                if (c is ButtonBase && string.IsNullOrEmpty(c.Text) && !string.IsNullOrEmpty(c.AccessibleName)) continue;
                char? key = ControlTree.AccessKey(c.Text);
                if (key == null)
                {
                    Assert.True(ReferenceEquals(f.CancelButton, c), name + ": no access key on \"" + c.Text + "\"");
                    continue;
                }
                Assert.False(seen.ContainsKey(key.Value), name + ": '" + key.Value + "' is used twice (\"" + (seen.TryGetValue(key.Value, out string? other) ? other : "") + "\", \"" + c.Text + "\")");
                seen[key.Value] = c.Text;
            }
        }

        /// <summary>
        /// G10: window tests run on the test process's own desktop, which nobody sees, and never on the user's desktop. The
        /// desktop must exist (without it every window test fails instead of falling back) and be the one the test thread
        /// uses.
        /// </summary>
        [Fact]
        public void Window_tests_run_on_a_desktop_nobody_sees()
        {
            string? testDesktop = TestDesktop.Name;
            Assert.NotNull(testDesktop);
            Assert.StartsWith("QuietHosts.Tests.", testDesktop, StringComparison.Ordinal);
            string used = string.Empty;
            Sta.Run(() => used = TestDesktop.CurrentName());
            Assert.Equal(testDesktop, used);
            Assert.NotEqual("Default", used);
        }

        /// <summary>
        /// A window may grow as tall as its pretend screen, whatever the real screen is. Windows keeps a window about as tall
        /// as the real screen: on a CI runner's 1024x768 screen the 841-pixel main window with three notices came out about
        /// 780 pixels tall, so a test measured a cut window.
        /// </summary>
        [Fact]
        public void A_window_may_grow_as_tall_as_its_pretend_screen()
        {
            Sta.Run(() =>
            {
                int limit = SystemInformation.MaxWindowTrackSize.Height;
                using (var w = new TestWindow())
                {
                    w.Add(new Panel { Size = new Size(200, limit + 100) });
                    w.PreviewWorkingArea = new Rectangle(0, 0, 2000, limit + 400);
                    Sta.ShowOffScreen(w);
                    Assert.True(w.Height > limit + 100, "the window is " + w.Height + " px tall; Windows' own limit is " + limit + " px");
                    w.Close();
                }
            });
        }

        [Fact]
        public void The_main_window_shows_every_state()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (ProtectionState state in Enum.GetValues(typeof(ProtectionState)))
                    {
                        foreach (RefusalReason refusal in Enum.GetValues(typeof(RefusalReason)))
                        {
                            HostsStatus s = UiScenarios.Status(p.Lists, state, refusal: refusal);
                            using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false))
                            {
                                List<Control> all = ControlTree.All(f).ToList();
                                string headline = Strings.Headline(s.State, s.Refusal);
                                Assert.Contains(all, c => c is Label && c.Text == headline);
                                Assert.Contains(all, c => c is Label && c.Text == Strings.Detail(s));
                                Button main = all.OfType<Button>().Single(b => b.Text == Strings.MainButton(state));
                                Assert.Contains(Strings.Detail(s), main.AccessibleDescription);
                                ShieldIcon icon = all.OfType<ShieldIcon>().Single();
                                Assert.Equal(headline, icon.AccessibleName);
                                Assert.Equal(ShieldIcon.KindFor(state), icon.Kind);
                                Assert.Null(f.AcceptButton);
                            }
                        }
                    }
                });
            }
        }

        [Fact]
        public void The_main_window_lists_warnings_tips_and_level_descriptions()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    HostsWarnings all = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
                        HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: all, legacy: true);
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false))
                    {
                        // Every warning and tip is a notice, shown or behind "Show N more notices".
                        IReadOnlyList<string> notices = f.PreviewNoticeTexts;
                        foreach (HostsWarnings w in Enum.GetValues(typeof(HostsWarnings)))
                        {
                            if (w != HostsWarnings.None) Assert.Contains(Strings.Warning(w, s.BytesOutsideBlocks), notices);
                        }
                        Assert.Contains(Strings.TipRunningFromDownloads, notices);
                        Assert.Contains(Strings.TipRunningFromTemp, notices);
                        List<string> shown = ControlTree.All(f).OfType<Label>().Where(l => ControlTree.IsMeantToShow(l, f)).Select(l => l.Text).ToList();
                        Assert.Contains(Strings.BlockListFrom(p.Lists.Version), shown);
                        Assert.Contains(Strings.StandardDescription, shown);
                        Assert.Contains(Strings.StrictDescription(p.Lists.StrictExtraCount), shown);

                        // The level cards: Standard is in use; "Use Strict…" describes Strict.
                        Assert.Equal(LevelCardState.InUse, f.PreviewStandardCard.View!.State);
                        Assert.True(ControlTree.IsMeantToShow(f.PreviewStandardCard.Indicator, f));
                        Assert.Equal(Strings.InUseIndicator, f.PreviewStandardCard.Indicator.Text);
                        Assert.Equal(Strings.UseStrictCardButton, f.PreviewStrictCard.Button.Text);
                        Assert.Equal(Strings.StrictDescription(p.Lists.StrictExtraCount), f.PreviewStrictCard.Button.AccessibleDescription);
                        Assert.Empty(ControlTree.All(f).OfType<RadioButton>());
                    }
                });
            }
        }

        [Fact]
        public void A_block_from_an_older_list_gets_a_tip()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus older = UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: "2000-01-01");
                    using (var f = new MainForm(new AppController(p.Context, older), showFirstRun: false))
                    {
                        List<string> shown = ControlTree.All(f).OfType<Label>().Where(l => ControlTree.IsMeantToShow(l, f)).Select(l => l.Text).ToList();
                        Assert.Contains(Strings.TipNewerListInApp(p.Lists.Version), shown);
                        Assert.Contains(Strings.BlockListFrom("2000-01-01"), shown);
                    }
                    HostsStatus current = UiScenarios.Status(p.Lists, ProtectionState.On);
                    using (var f = new MainForm(new AppController(p.Context, current), showFirstRun: false))
                    {
                        Assert.DoesNotContain(ControlTree.All(f).OfType<Label>(), l => l.Text == Strings.TipNewerListInApp(p.Lists.Version) && ControlTree.IsMeantToShow(l, f));
                    }
                });
            }
        }

        [Fact]
        public void The_first_run_text_replaces_the_normal_content()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.OlderList, firstRunDone: false);
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: true))
                    {
                        Assert.True(f.IsShowingFirstRun);
                        List<Control> shown = ControlTree.All(f).Where(c => ControlTree.IsMeantToShow(c, f)).ToList();
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunTitle);
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunLegacy);
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunFact1Body(p.Lists.StandardCount));
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunFact2Unsigned);
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunFact3Body);
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunLimit);
                        Assert.Contains(shown, c => c.Text == Strings.FirstRunTrust);
                        Assert.DoesNotContain(shown, c => c.Text == Strings.LevelHeading);
                        Assert.DoesNotContain(shown, c => c.Text == Strings.FixButton);
                    }
                });
            }
        }

        [Fact]
        public void The_status_line_shows_progress_then_the_result()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off));
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.True(c.IsBusy);
                        // The progress row under the main button: the ring and the text.
                        Assert.Contains(ControlTree.All(f).OfType<Label>(), l => l.Text == Strings.WaitingForPermission && ControlTree.IsMeantToShow(l, f));
                        Assert.True(ControlTree.IsMeantToShow(f.PreviewStatusCard.Ring, f));
                        Assert.True(f.PreviewStatusCard.Ring.Active);
                        Assert.False(f.PreviewStandardCard.Button.Enabled);
                        Assert.False(f.PreviewStrictCard.Button.Enabled);
                        Assert.False(ControlTree.All(f).OfType<Button>().Single(b => b.Text == Strings.MainButton(ProtectionState.Off)).Enabled);
                    }

                    var done = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var f = new MainForm(done, showFirstRun: false))
                    {
                        done.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.Equal(Strings.ResultTurnOnOk, done.StatusLineText);
                        Assert.Contains(ControlTree.All(f).OfType<Label>(), l => l.Text == Strings.ResultTurnOnOk);
                    }
                });
            }
        }

        [Fact]
        public void Preview_controllers_never_start_a_change()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off));
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        System.Threading.Tasks.Task run = c.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.TurnOn, f);
                        Assert.True(run.IsCompleted);
                        Assert.False(c.IsBusy);
                        Assert.Null(c.ResultLine);
                    }
                });
            }
        }

        [Fact]
        public void The_fix_dialog_searches_and_limits_the_selection()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.Equal(p.Lists.StandardCount, d.TotalRows);
                        Assert.Equal(d.TotalRows, d.ShownRows);
                        Assert.Equal(Strings.FixShowing(d.TotalRows, d.TotalRows), d.FooterText);
                        Assert.False(d.CanAllow);
                        Assert.False(d.CanBlockAgain);

                        d.PreviewSearch("doubleclick");
                        Assert.InRange(d.ShownRows, 1, d.TotalRows - 1);
                        Assert.Equal(Strings.FixShowing(d.ShownRows, d.TotalRows), d.FooterText);

                        d.PreviewSearch("no-such-site-anywhere");
                        Assert.Equal(0, d.ShownRows);
                        Assert.Equal(Strings.FixNoMatch("no-such-site-anywhere"), d.FooterText);

                        d.PreviewSearch("");
                        d.PreviewSelect(1);
                        Assert.True(d.CanAllow);
                        Assert.False(d.CanBlockAgain);
                        Assert.Equal(Strings.FixConsequence, d.FooterText);

                        d.PreviewSearch("");
                        d.PreviewSelect(Limits.MaxHostsPerRun + 1);
                        Assert.False(d.CanAllow);
                        Assert.Equal(Strings.FixTooMany(Limits.MaxHostsPerRun), d.FooterText);
                    }
                });
            }
        }

        [Fact]
        public void The_fix_dialog_lists_allowed_addresses_first_and_can_block_them_again()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3);
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.Equal(p.Lists.StrictTotalCount, d.TotalRows);
                        ListView list = ControlTree.All(d).OfType<ListView>().Single();
                        Assert.Equal(Strings.FixListAccessibleName, list.AccessibleName);
                        for (int i = 0; i < 3; i++) Assert.Equal(Strings.StatusAllowed, list.Items[i].SubItems[FixDialog.StatusColumn].Text);
                        Assert.Equal(Strings.StatusBlocked, list.Items[3].SubItems[FixDialog.StatusColumn].Text);
                        d.PreviewSelect(2);
                        Assert.True(d.CanBlockAgain);
                        Assert.False(d.CanAllow);
                        Assert.Contains(ControlTree.All(d), c => c.Text == Strings.FixSwitchToStandardButton && ControlTree.IsMeantToShow(c, d));
                        TextBox search = ControlTree.All(d).OfType<TextBox>().Single();
                        Assert.Equal(Strings.FixSearchAccessibleName, search.AccessibleName);
                    }
                });
            }
        }

        [Fact]
        public void The_fix_dialog_needs_protection_on_to_change_addresses()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off))))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.Equal(Strings.FixTurnOnToChange, d.FooterText);
                        d.PreviewSelect(1);
                        Assert.False(d.CanAllow);
                        List<Control> shown = ControlTree.All(d).Where(c => ControlTree.IsMeantToShow(c, d)).ToList();
                        Assert.Contains(shown, c => c.Text == Strings.FixStepOffBody);
                        Assert.Contains(shown, c => c.Text == Strings.FixTurnOnProtectionButton);
                        Assert.DoesNotContain(shown, c => c.Text == Strings.FixSwitchToStandardButton);
                    }
                });
            }
        }

        [Fact]
        public void Dialog_defaults_follow_the_spec()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var undo = new UndoDialog(UiScenarios.Status(p.Lists, ProtectionState.On)))
                    {
                        // Cancel is the default and has the focus.
                        Assert.Same(undo.CancelButton, undo.AcceptButton);
                        Assert.Equal(Strings.CancelButton, ((Button)undo.AcceptButton!).Text);
                        Assert.Same(undo.AcceptButton, undo.ActiveControl);
                    }
                    using (var strict = new StrictDialog(p.Context, StrictDialogMode.Confirm, elevates: true))
                    {
                        Assert.Equal(Strings.UseStrictButton, ((Button)strict.AcceptButton!).Text);
                        Assert.Equal(Strings.CancelButton, ((Button)strict.CancelButton!).Text);
                    }
                    using (var info = new StrictDialog(p.Context, StrictDialogMode.Info, elevates: false))
                    {
                        Assert.Equal(Strings.CloseButton, ((Button)info.CancelButton!).Text);
                        Assert.DoesNotContain(ControlTree.All(info), c => c.Text == Strings.UseStrictButton);
                    }
                    using (var error = new ErrorDialog(p.Context, ResultKind.FileLocked, false, () => "details"))
                    {
                        Assert.Equal(Strings.TryAgainButton, ((Button)error.AcceptButton!).Text);
                        Assert.Contains(ControlTree.All(error), c => c.Text == Strings.ErrorBody(ResultKind.FileLocked, false));
                    }
                    // Unexpected with the file unchanged: Try again (the default) and Report on GitHub.
                    using (var error = new ErrorDialog(p.Context, ResultKind.Unexpected, false, () => "details"))
                    {
                        Assert.Contains(ControlTree.All(error), c => c.Text == Strings.UnexpectedUnchanged);
                        Assert.Contains(ControlTree.All(error), c => c is Button && c.Text == Strings.ReportOnGitHubButton);
                        Assert.Equal(Strings.TryAgainButton, ((Button)error.AcceptButton!).Text);
                    }
                    // Unexpected with the file maybe incomplete: Repair is the default.
                    using (var error = new ErrorDialog(p.Context, ResultKind.Unexpected, null, () => "details"))
                    {
                        Assert.Contains(ControlTree.All(error), c => c.Text == Strings.UnexpectedMayBeIncomplete);
                        Assert.Equal(Strings.RepairButton, ((Button)error.AcceptButton!).Text);
                        Assert.DoesNotContain(ControlTree.All(error), c => c.Text == Strings.TryAgainButton);
                    }
                    // A problem in the window itself: Close is the default, and there is nothing to try again.
                    using (var error = new ErrorDialog(p.Context, new InvalidOperationException("example")))
                    {
                        Assert.Same(error.CancelButton, error.AcceptButton);
                        Assert.Equal(Strings.CloseButton, ((Button)error.AcceptButton!).Text);
                        Assert.DoesNotContain(ControlTree.All(error), c => c.Text == Strings.TryAgainButton || c.Text == Strings.RepairButton);
                    }
                });
            }
        }

        [Fact]
        public void The_license_and_icon_are_embedded()
        {
            string text = LicenseDialog.LoadText();
            Assert.Contains("MIT License", text);
            Assert.Contains("QuietHosts contributors", text);
            string shown = LicenseDialog.ForDisplay("MIT License\n\nline one\nline two\n\nlast\n");
            Assert.Equal("MIT License\r\n\r\nline one line two\r\n\r\nlast", shown);
            using (Icon? icon = AppIcon.Load())
            {
                Assert.NotNull(icon);
            }
        }

        [Fact]
        public void Copy_details_masks_the_profile_path_and_names_the_result()
        {
            using (var p = new PreviewContext())
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var r = new LaunchResult(ResultKind.Unexpected, 15, new InvalidOperationException("failed in " + profile + @"\x"), new string('a', 64), new string('a', 64));
                HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On);
                string details = DetailsReport.Build(p.Context, ElevatedCommand.Allow(new[] { "a.ads-one.net", "b.ads-two.com" }), r, s, null);
                Assert.DoesNotContain(profile, details, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("%USERPROFILE%", details);
                Assert.Contains("allow (2 addresses)", details);
                Assert.DoesNotContain("a.ads-one.net", details);
                Assert.Contains(Strings.DetailsResult + ": Unexpected", details);
                Assert.Contains(Strings.DetailsHostsFile + ": " + Strings.DetailsHostsUnchanged, details);
                Assert.Contains(AppInfo.Version, details);
                Assert.Contains(p.Lists.Version, details);
                Assert.Contains(UiScenarios.FakeLastRun.Split('\n')[0], details);
            }
        }

        [Fact]
        public void The_status_icon_uses_window_text_in_high_contrast()
        {
            Theme hc = Theme.ForHighContrast(HighContrastPalette.System);
            foreach (ShieldKind kind in new[] { ShieldKind.On, ShieldKind.Off, ShieldKind.Attention })
            {
                Assert.Equal(SystemColors.WindowText, ShieldPainter.FillFor(kind, hc));
                Assert.Equal(SystemColors.Window, ShieldPainter.SymbolFor(kind, hc));
            }
            Assert.Equal(ShieldKind.On, ShieldIcon.KindFor(ProtectionState.On));
            Assert.Equal(ShieldKind.Off, ShieldIcon.KindFor(ProtectionState.Off));
            foreach (ProtectionState s in new[] { ProtectionState.Removed, ProtectionState.OlderList, ProtectionState.CantRead, ProtectionState.Refused })
                Assert.Equal(ShieldKind.Attention, ShieldIcon.KindFor(s));

            // Light and dark: the token table's values.
            Assert.Equal(0x0F7B0F, ShieldPainter.FillFor(ShieldKind.On, Theme.Light).ToArgb() & 0xFFFFFF);
            Assert.Equal(0x6E6E6E, ShieldPainter.FillFor(ShieldKind.Off, Theme.Light).ToArgb() & 0xFFFFFF);
            Assert.Equal(0x9D5D00, ShieldPainter.FillFor(ShieldKind.Attention, Theme.Light).ToArgb() & 0xFFFFFF);
            Assert.Equal(0xFFFFFF, ShieldPainter.SymbolFor(ShieldKind.On, Theme.Light).ToArgb() & 0xFFFFFF);
            Assert.Equal(0x6CCB5F, ShieldPainter.FillFor(ShieldKind.On, Theme.Dark).ToArgb() & 0xFFFFFF);
            Assert.Equal(0x9F9F9F, ShieldPainter.FillFor(ShieldKind.Off, Theme.Dark).ToArgb() & 0xFFFFFF);
            Assert.Equal(0xFCE100, ShieldPainter.FillFor(ShieldKind.Attention, Theme.Dark).ToArgb() & 0xFFFFFF);
            Assert.Equal(0x000000, ShieldPainter.SymbolFor(ShieldKind.On, Theme.Dark).ToArgb() & 0xFFFFFF);
        }
    }
}
