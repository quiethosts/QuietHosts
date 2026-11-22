using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// The small dialogs (spec 6, T-C-14..20): Strict, Undo, the error dialogs, the open-failure dialog, About, How it works,
    /// and what every dialog gives screen readers and high contrast.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class DialogTests
    {
        private static bool Shows(Form f, Control c) => ControlTree.IsMeantToShow(c, f);

        private static List<string> RightButtons(ThemedDialog d) =>
            ControlTree.All(d).OfType<CommandBar>().Single().RightControls.Select(c => c.Text).ToList();

        private static CommandBar FooterOf(Form d) => ControlTree.All(d).OfType<CommandBar>().Single();

        // ---------------------------------------------------------------- T-C-14: the Strict dialogs (6.2)

        [Fact]
        public void The_strict_dialogs_list_four_effects_and_the_shield_follows_protection()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (bool elevates in new[] { true, false })
                    {
                        using (var d = new StrictDialog(p.Context, StrictDialogMode.Confirm, elevates))
                        {
                            Assert.Equal(Strings.ErrorWindowTitle, d.Text);
                            Assert.Equal(Strings.StrictConfirmTitle, d.Heading.Text);
                            Assert.Equal(TextStyle.Subtitle, d.Heading.Style);
                            Assert.Equal(Strings.StrictIntro(p.Lists.StrictExtraCount), d.Intro.Text);
                            Assert.Equal(Strings.StrictEffects, d.Effects.Select(l => l.Text));
                            Assert.Equal(4, d.Effects.Count);
                            Assert.Equal(Strings.StrictOutro, d.Outro.Text);
                            // Use Strict: accent, the default and the first focus; the shield only while protection is on.
                            ThemedButton use = d.UseStrictControl!;
                            Assert.Equal(Strings.UseStrictButton, use.Text);
                            Assert.Same(use, d.AcceptButton);
                            Assert.Same(use, d.ActiveControl);
                            Assert.Equal(ButtonStyle.Accent, use.Style);
                            Assert.Equal(elevates, use.Shield);
                            Assert.Equal(elevates, d.Elevates);
                            Assert.Equal(DialogResult.OK, use.DialogResult);
                            Assert.Same(d.CancelControl, d.CancelButton);
                            Assert.Equal(Strings.CancelButton, d.CancelControl.Text);
                            Assert.Equal(new[] { Strings.UseStrictButton, Strings.CancelButton }, RightButtons(d));
                            Assert.Equal(StrictDialogMode.Confirm, d.Mode);
                        }
                    }
                    using (var d = new StrictDialog(p.Context, StrictDialogMode.Info, elevates: true))
                    {
                        Assert.Equal(Strings.StrictInfoTitle, d.Heading.Text);
                        Assert.Equal(4, d.Effects.Count);
                        Assert.Null(d.UseStrictControl);
                        Assert.False(d.Elevates);
                        // Close only: accent, the default, Esc, the first focus.
                        Assert.Equal(new[] { Strings.CloseButton }, RightButtons(d));
                        Assert.Same(d.CancelControl, d.AcceptButton);
                        Assert.Same(d.CancelControl, d.CancelButton);
                        Assert.Same(d.CancelControl, d.ActiveControl);
                        Assert.Equal(ButtonStyle.Accent, d.CancelControl.Style);
                    }
                });
            }
        }

#if DEBUG
        [Fact]
        public void At_the_largest_text_size_every_effect_keeps_its_bullet_and_the_list_fits()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (float text in new[] { 1f, 2.25f })
                        {
                            UiFonts.TextScale = text;
                            using (var d = new StrictDialog(p.Context, StrictDialogMode.Info, elevates: false))
                            {
                                Sta.ShowOffScreen(d);
                                var table = (TableLayoutPanel)d.Effects[0].Parent!;
                                for (int row = 0; row < d.Effects.Count; row++)
                                {
                                    Control bullet = table.GetControlFromPosition(0, row)!;
                                    Assert.Equal(Strings.Bullet, bullet.Text);
                                    // The bullet's label gets the width it needs (narrower, it draws nothing), and the item's text
                                    // starts after it.
                                    Assert.True(bullet.Width >= TextRenderer.MeasureText(bullet.Text, bullet.Font).Width, text + "x text: the bullet has no room");
                                    Assert.True(d.Effects[row].Left >= bullet.Right, text + "x text: the text overlaps the bullet");
                                }
                                Assert.True(table.Width <= UiScale.Current.Px(DialogParts.ContentWidth), text + "x text: the list is wider than the dialog's content");
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
#endif

        // ---------------------------------------------------------------- T-C-15: Undo (6.3)

        [Fact]
        public void Undo_always_has_the_shield_and_enter_cancels()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    string one = p.Lists.DefaultEntries.First().Host;
                    foreach ((HostsStatus s, int count) in new[]
                    {
                        (UiScenarios.Status(p.Lists, ProtectionState.On), p.Lists.StandardCount),
                        (UiScenarios.Status(p.Lists, ProtectionState.On, blockedHosts: new[] { one }), 1),
                        (UiScenarios.Status(p.Lists, ProtectionState.Off, allowed: 2), 0),
                    })
                    {
                        Assert.Equal(count, s.BlockedCount);
                        using (var d = new UndoDialog(s))
                        {
                            Assert.Equal(Strings.ErrorWindowTitle, d.Text);
                            Assert.Equal(Strings.UndoTitle, d.Heading.Text);
                            Assert.Equal(Strings.UndoBody(count, s.HasLegacyBlock), d.Message.Text);
                            // Remove everything: always the shield (Undo always starts an elevated run, D2), and no default.
                            Assert.True(d.RemoveControl.Shield);
                            Assert.Equal(ButtonStyle.Standard, d.RemoveControl.Style);
                            Assert.Equal(DialogResult.OK, d.RemoveControl.DialogResult);
                            Assert.Equal(new[] { Strings.RemoveEverythingButton, Strings.CancelButton }, RightButtons(d));
                            // Cancel: accent, the default, Esc and the first focus, so Enter right after opening cancels.
                            Assert.Same(d.CancelControl, d.AcceptButton);
                            Assert.Same(d.CancelControl, d.CancelButton);
                            Assert.Equal(ButtonStyle.Accent, d.CancelControl.Style);
                            Sta.ShowOffScreen(d);
                            Assert.Same(d.CancelControl, d.ActiveControl);
                            Assert.True(Flow.Enter(d, Keys.Enter));
                            Assert.Equal(DialogResult.Cancel, d.DialogResult);
                            d.Close();
                        }
                    }
                    Assert.StartsWith("This removes 1 blocked address from the hosts file", Strings.UndoBody(1, false), StringComparison.Ordinal);
                });
            }
        }

        // ---------------------------------------------------------------- T-C-16: the error dialogs (6.4)

        public static IEnumerable<object[]> ErrorKinds() => new[]
        {
            // kind, file changed, the buttons left to right, the default
            new object[] { ResultKind.FileLocked, false, new[] { Strings.TryAgainButton, Strings.CloseButton }, Strings.TryAgainButton },
            new object[] { ResultKind.AccessDenied, false, new[] { Strings.TryAgainButton, Strings.CloseButton }, Strings.TryAgainButton },
            new object[] { ResultKind.Reverted, true, new[] { Strings.TryAgainButton, Strings.CloseButton }, Strings.TryAgainButton },
            new object[] { ResultKind.Unexpected, false, new[] { Strings.TryAgainButton, Strings.ReportOnGitHubButton, Strings.CloseButton }, Strings.TryAgainButton },
            new object[] { ResultKind.Unexpected, true, new[] { Strings.RepairButton, Strings.ReportOnGitHubButton, Strings.CloseButton }, Strings.RepairButton },
        };

        [Theory]
        [MemberData(nameof(ErrorKinds))]
        public void Each_error_dialog_has_its_texts_buttons_and_copy_details(ResultKind kind, bool changed, string[] buttons, string defaultButton)
        {
            using (var p = new PreviewContext())
            {
                var heard = new List<(string Text, Announcement Kind)>();
                Sta.Run(() =>
                {
                    global::QuietHosts.Ui.AnnouncementSink = (c, text, k) => heard.Add((text, k));
                    try
                    {
                        string? copied = null;
                        using (var d = new ErrorDialog(p.Context, kind, changed, () => "the details") { PreviewClipboard = t => { copied = t; return true; } })
                        {
                            // A button clicks only in a window that shows.
                            Sta.ShowOffScreen(d);
                            Assert.Equal(Strings.ErrorWindowTitle, d.Text);
                            Assert.Equal(Strings.ErrorTitle(kind), d.Heading.Text);
                            Assert.Equal(Strings.ErrorBody(kind, changed), d.Message.Text);
                            Assert.Equal(Glyph.Error, d.ErrorGlyph.Glyph);
                            Assert.Equal(AccessibleRole.Graphic, d.ErrorGlyph.AccessibleRole);
                            Assert.Equal(Strings.ErrorIconName, d.ErrorGlyph.AccessibleName);
                            Assert.Equal(ThemeToken.CriticalGlyph, d.ErrorGlyph.ColorToken);
                            Assert.Equal(buttons, RightButtons(d));
                            Assert.Equal(defaultButton, ((Button)d.AcceptButton!).Text);
                            Assert.Same(d.AcceptButton, d.ActiveControl);
                            Assert.Same(d.CloseControl, d.CancelButton);
                            // Try again and Repair: accent, the shield, and Retry (the same change again).
                            ThemedButton retry = d.RetryControl!;
                            Assert.Equal(ButtonStyle.Accent, retry.Style);
                            Assert.True(retry.Shield);
                            Assert.Equal(DialogResult.Retry, retry.DialogResult);
                            Assert.True(ErrorDialog.IsRetryable(kind));
                            if (d.ReportControl != null)
                            {
                                Assert.True(d.ReportControl.External);
                                string? opened = null;
                                d.PreviewOpenUrl = url => opened = url;
                                d.ReportControl.PerformClick();
                                Assert.Equal(Strings.ReportBugUrl, opened);
                            }
                            // Copy details in every error dialog: the footer's left side; its feedback under the body, announced.
                            Assert.Same(d.CopyLink, Assert.Single(FooterOf(d).LeftControls));
                            Assert.Equal(Strings.CopyDetailsLink, d.CopyLink.Text);
                            Assert.False(Shows(d, d.Feedback));
                            d.CopyLink.PerformClick();
                            Assert.Equal("the details", copied);
                            Assert.True(Shows(d, d.Feedback));
                            Assert.Equal(Strings.DetailsCopied, d.Feedback.Text);
                            Assert.Contains((Strings.DetailsCopied, Announcement.Result), heard);
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
        public void A_problem_in_the_window_offers_no_try_again()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new ErrorDialog(p.Context, new InvalidOperationException("example")) { PreviewClipboard = t => false })
                    {
                        Assert.Equal(Strings.WindowErrorTitle, d.Heading.Text);
                        Assert.Equal(Strings.WindowErrorBody, d.Message.Text);
                        Assert.Null(d.RetryControl);
                        Assert.Equal(new[] { Strings.ReportOnGitHubButton, Strings.CloseButton }, RightButtons(d));
                        Assert.Same(d.CloseControl, d.AcceptButton);
                        Assert.Equal(ButtonStyle.Accent, d.CloseControl.Style);
                        d.CopyLink.PerformClick();
                        Assert.Equal(Strings.DetailsNotCopied, d.Feedback.Text);
                    }
                    // The details of a window problem: the exception's type and message, never its file paths.
                    string details = DetailsReport.Build(p.Context, null, null, null, new InvalidOperationException("example"));
                    Assert.Contains(Strings.DetailsError + ": System.InvalidOperationException: example", details);
                });
            }
        }

        [Fact]
        public void Repair_and_try_again_run_the_same_change_again()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    // Unexpected with the file changed (Repair), then FileLocked (Try again), then it works.
                    var results = new Queue<LaunchResult>(new[]
                    {
                        new LaunchResult(ResultKind.Unexpected, 15, null, Flow.Hash, Flow.OtherHash),
                        new LaunchResult(ResultKind.FileLocked, 11, null, Flow.Hash, Flow.Hash),
                        new LaunchResult(ResultKind.Success, 0, null, Flow.Hash, Flow.OtherHash),
                    });
                    var commands = new List<ElevatedCommand>();
                    LaunchChange launch = (cmd, progress, owner) =>
                    {
                        commands.Add(cmd);
                        LaunchResult r = results.Dequeue();
                        return Task.Run(() => r);
                    };
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 1);
                    var c = new AppController(p.Context, s, launch, () => s);
                    using (var main = new MainForm(c, showFirstRun: false))
                    using (var watch = new DialogWatch(d => DialogResult.Retry))
                    {
                        watch.Describe = d => d is ErrorDialog e ? ((Button)e.AcceptButton!).Text : d.GetType().Name;
                        Sta.ShowOffScreen(main);
                        ElevatedCommand block = ElevatedCommand.Block(new[] { s.Allowlist[0] });
                        Task run = Flow.Start(main, () => c.RunAsync(block, ChangeKind.Block, main));
                        Flow.PumpUntil(() => run.IsCompleted, 10000);
                        Assert.True(run.IsCompleted);
                        Assert.Equal(new[] { Strings.RepairButton, Strings.TryAgainButton }, watch.Answered);
                        Assert.Equal(3, commands.Count);
                        Assert.All(commands, cmd => Assert.Same(block, cmd));
                        Assert.Equal(Strings.ResultBlocked(1), c.ResultLine);
                        main.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-17: the open-failure dialog (6.5)

        [Fact]
        public void A_link_or_folder_that_doesnt_open_shows_quiethosts_own_dialog()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var seen = new List<string>();
                    Links.PreviewStart = psi => throw new Win32Exception(2);
                    try
                    {
                        using (var host = new TestWindow())
                        using (var watch = new DialogWatch(d =>
                        {
                            var o = (OpenFailedDialog)d;
                            seen.Add(o.Heading.Text + "|" + o.Message.Text + "|" + o.Target.TextBox.Text + "|" + o.Target.TextBox.AccessibleName);
                            return DialogResult.Cancel;
                        }))
                        {
                            Sta.ShowOffScreen(host);
                            Task run = Flow.Start(host, () =>
                            {
                                Links.OpenUrl(p.Context, host, Strings.ReleasesUrl);
                                Links.OpenFolder(p.Context, host, HostsDatabase.DefaultFolder);
                                return Task.CompletedTask;
                            });
                            Flow.PumpUntil(() => run.IsCompleted && seen.Count == 2);
                            host.Close();
                        }
                    }
                    finally
                    {
                        Links.PreviewStart = null;
                    }
                    Assert.Equal(new[]
                    {
                        Strings.LinkFailedTitle + "|" + Strings.LinkFailedBody + "|" + Strings.ReleasesUrl + "|Copy this address into your browser",
                        Strings.FolderFailedTitle + "|" + Strings.FolderFailedBody + "|" + HostsDatabase.DefaultFolder + "|This is the folder",
                    }, seen);

                    // A preview without the hook starts nothing and shows nothing.
                    Links.OpenUrl(p.Context, null, Strings.ReleasesUrl);

                    // Copy puts the address on the clipboard; the line under the box says whether it worked.
                    string? copied = null;
                    using (var d = new OpenFailedDialog(p.Context, Strings.ProjectUrl, isFolder: false) { PreviewClipboard = t => { copied = t; return true; } })
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(Strings.ErrorWindowTitle, d.Text);
                        Assert.True(d.Target.TextBox.ReadOnly);
                        Assert.Equal(new[] { Strings.CopyButton, Strings.CloseButton }, RightButtons(d));
                        Assert.Same(d.CloseControl, d.AcceptButton);
                        Assert.Same(d.CloseControl, d.CancelButton);
                        Assert.Equal(ButtonStyle.Accent, d.CloseControl.Style);
                        d.CopyControl.PerformClick();
                        Assert.Equal(Strings.ProjectUrl, copied);
                        Assert.Equal(Strings.TextCopied, d.Feedback.Text);
                    }
                    using (var d = new OpenFailedDialog(p.Context, Strings.ProjectUrl, isFolder: false) { PreviewClipboard = t => false })
                    {
                        d.PreviewCopy();
                        Assert.Equal(Strings.DetailsNotCopied, d.Feedback.Text);
                    }
                });
            }
        }

        [Fact]
        public void A_long_address_wraps_in_the_open_failure_dialog_so_all_of_it_shows()
        {
            // The one-line box cut most of the app's GitHub addresses at its edge, with no sign that text was hidden (visual-7):
            // the address wraps, and the box is as tall as its lines, three at most, then it scrolls.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach ((string target, int minLines) in new[] { (Strings.ReleasesUrl, 1), (Strings.ReportSiteUrl, 2), (Strings.DefenderHelpUrl, 2) })
                    {
                        using (var d = new OpenFailedDialog(p.Context, target, isFolder: false))
                        {
                            Sta.ShowOffScreen(d);
                            TextBox box = d.Target.TextBox;
                            Assert.True(box.Multiline && box.WordWrap && box.ReadOnly);
                            Assert.Equal(target, box.Text);
                            int lines = NativeTheme.EditLineCount(box);
                            Assert.InRange(lines, minLines, OpenFailedDialog.MaxTargetLines);
                            Assert.Equal(lines, d.Target.ShownLines);
                            Assert.Equal(ScrollBars.None, box.ScrollBars);
                            Assert.True(box.ClientSize.Height >= lines * box.Font.Height, target + ": " + lines + " lines in " + box.ClientSize.Height + " px");
                            Assert.Equal(DialogParts.ContentWidth, (int)Math.Round(d.Target.Width * 96.0 / UiScale.Current.Dpi));
                            d.Close();
                        }
                    }
                    // Longer than three lines: three show, and the box scrolls.
                    using (var d = new OpenFailedDialog(p.Context, "https://example.com/" + new string('a', 400), isFolder: false))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.Equal(OpenFailedDialog.MaxTargetLines, d.Target.ShownLines);
                        Assert.Equal(ScrollBars.Vertical, d.Target.TextBox.ScrollBars);
                        d.Close();
                    }
                });
            }
        }

#if DEBUG
        [Fact]
        public void At_large_text_the_open_failure_box_takes_the_lines_its_text_box_counts()
        {
            // At 225% text the report address took four lines where three were estimated, and the box changed its scroll bar
            // while its handle was being created: that recreated the handle inside WM_CREATE and the dialog failed with "Error
            // creating window handle". The scroll bar now changes after that message, and the box ends up as its lines need.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (float text in new[] { 1.5f, 1.75f, 2f, 2.25f, 2.5f })
                        {
                            UiFonts.TextScale = text;
                            foreach ((string target, bool folder) in new[]
                            {
                                (Strings.ReportSiteUrl, false), (Strings.DefenderHelpUrl, false), (Strings.ReleasesUrl, false),
                                (@"C:\Program Files\QuietHosts\Backups\A folder with a rather long name for this test", true),
                            })
                            {
                                using (var d = new OpenFailedDialog(p.Context, target, isFolder: folder))
                                {
                                    Sta.ShowOffScreen(d);
                                    for (int i = 0; i < 4; i++) Application.DoEvents();
                                    TextBox box = d.Target.TextBox;
                                    string what = text + "x text, " + target + ": ";
                                    int lines = NativeTheme.EditLineCount(box);
                                    Assert.True(lines >= 1, what + "no lines");
                                    bool scrolls = lines > OpenFailedDialog.MaxTargetLines;
                                    Assert.True(scrolls == (box.ScrollBars == ScrollBars.Vertical), what + lines + " lines, scroll bars " + box.ScrollBars);
                                    Assert.Equal(Math.Min(lines, OpenFailedDialog.MaxTargetLines), d.Target.ShownLines);
                                    Assert.True(box.ClientSize.Height >= d.Target.ShownLines * box.Font.Height, what + d.Target.ShownLines + " lines in " + box.ClientSize.Height + " px");
                                    d.Close();
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
        }
#endif

        // ---------------------------------------------------------------- T-C-18: About (6.7)

        public static IEnumerable<object[]> Signatures() => new[]
        {
            // The kind as a number: SignatureKind is internal to the app, and a public test can't take it as a parameter.
            new object[] { (int)SignatureKind.Unsigned, null!, Strings.SignatureUnsigned },
            new object[] { (int)SignatureKind.Signed, "Example Publisher", Strings.SignatureSigned("Example Publisher") },
            new object[] { (int)SignatureKind.Unverified, null!, Strings.SignatureUnverified },
        };

        [Theory]
        [MemberData(nameof(Signatures))]
        public void About_has_its_sections_in_order_and_says_how_the_exe_is_signed(int signature, string? publisher, string expected)
        {
            var kind = (SignatureKind)signature;
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var ctx = new UiContext(p.Env, p.Lists, p.Context.ExePath, RunLocationTips.None, false, true, new SignatureState(kind, publisher));
                    using (var d = new AboutDialog(new AppController(ctx, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Assert.Equal(Strings.AboutTitle, d.Text);
                        Assert.Equal(Strings.AppName, d.AppName.Text);
                        Assert.Equal(Strings.AboutVersion(AppInfo.Version), d.Version.Text);
                        Assert.Equal(Strings.AppName, d.Icon48.AccessibleName);
                        Assert.Equal(new[] { Strings.AboutBlockListHeading, Strings.AboutPrivacyHeading, Strings.AboutHostsFileHeading, Strings.AboutSignatureHeading,
                            Strings.AboutLicenseHeading, Strings.AboutHelpHeading, Strings.AboutUninstallHeading }, d.SectionHeadings.Select(h => h.Text));
                        Assert.Equal(expected, d.SignatureText.Text);

                        // The links in order; the external ones say "Opens in your browser."; each is named by its text.
                        var links = d.AllLinks.ToList();
                        Assert.Equal(new[] { Strings.CheckForNewerVersionLink, Strings.OpenBackupFolderLink, Strings.HowItWorksLink, Strings.CheckDownloadLink,
                            Strings.ViewLicenseLink, Strings.ProjectPageLink, Strings.ReportProblemLink, Strings.AboutHelpLink }, links.Select(l => l.Text));
                        var external = new[] { Strings.CheckForNewerVersionLink, Strings.CheckDownloadLink, Strings.ProjectPageLink, Strings.ReportProblemLink, Strings.AboutHelpLink };
                        foreach (ThemedLink link in links)
                        {
                            Assert.Equal(external.Contains(link.Text), link.External);
                            Assert.Equal(Strings.WithoutAccessKey(link.Text), link.AccessibleName);
                            if (link.External) Assert.Equal(Strings.OpensInBrowser, link.AccessibleDescription);
                        }
                        // N, B, H, C, L, P, R, W: each once.
                        Assert.Equal(new char?[] { 'N', 'B', 'H', 'C', 'L', 'P', 'R', 'W' }, links.Select(l => ControlTree.AccessKey(l.Text)));

                        // Where they go (a preview opens nothing).
                        var opened = new List<string>();
                        d.PreviewOpenUrl = url => opened.Add(url);
                        foreach (ThemedLink link in links.Where(l => l.External || l.Text == Strings.OpenBackupFolderLink)) link.PerformClick();
                        Assert.Equal(new[] { Strings.ReleasesUrl, HostsDatabase.DefaultFolder, Strings.VerifyDownloadUrl, Strings.ProjectUrl, Strings.ReportBugUrl, Strings.HelpUrl }, opened);

                        // Close: accent, the default, Esc, the first focus.
                        Assert.Same(d.CloseControl, d.AcceptButton);
                        Assert.Same(d.CloseControl, d.CancelButton);
                        Assert.Same(d.CloseControl, d.ActiveControl);
                        Assert.Equal(ButtonStyle.Accent, d.CloseControl.Style);
                    }
                });
            }
            Assert.Equal(expected, AboutDialog.SignatureLine(new SignatureState(kind, publisher)));
        }

        // ---------------------------------------------------------------- T-C-19: How it works (6.9)

        [Fact]
        public void How_it_works_opens_at_its_start_section()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    Assert.Equal(new[] { Strings.HowWhatHeading, Strings.HowAskHeading, Strings.HowRestartHeading, Strings.HowCantBlockHeading,
                        Strings.HowBackupsHeading, Strings.HowFolderHeading, Strings.HowUninstallHeading }, HowItWorksDialog.Sections().Select(x => x.Heading));
                    Assert.Equal(new[] { Strings.HowCantBlockBody, Strings.HowCantBlockMore },
                        HowItWorksDialog.Sections().Single(x => x.Section == HowItWorksSection.CantBlock).Texts);

                    var cases = new List<(HowItWorksSection Start, Rectangle Screen)>();
                    foreach (HowItWorksSection start in new[] { HowItWorksSection.Top, HowItWorksSection.WhyWindowsAsks, HowItWorksSection.CantBlock, HowItWorksSection.OwnFolder, HowItWorksSection.Uninstall })
                        cases.Add((start, UiScenarios.Scaled(UiScenarios.ShortScreen)));
                    // A laptop screen, where the sections near the end can't scroll to the top by themselves (visual-12).
                    cases.Add((HowItWorksSection.OwnFolder, UiScenarios.Scaled(UiScenarios.LaptopScreen)));
                    foreach ((HowItWorksSection start, Rectangle screen) in cases)
                    {
                        using (var d = new HowItWorksDialog(p.Context, start) { PreviewWorkingArea = screen })
                        {
                            Assert.Equal(Strings.HowItWorksTitle, d.Text);
                            Assert.Equal(start, d.Start);
                            Sta.ShowOffScreen(d);
                            Assert.True(d.ContentScrolls);
                            ScrollHost host = ControlTree.All(d).OfType<ScrollHost>().Single();
                            ThemedLabel heading = d.HeadingOf(start);
                            // The start section's heading at the top of the visible area, with the 16 epx gap above every
                            // heading (so no line of the section before shows cut), also for a section near the end: the
                            // content gets the room after its last section that it needs for that.
                            int expected = start == HowItWorksSection.Top ? 0 : heading.Top - UiScale.Current.Px(16);
                            Assert.Equal(expected, d.PreviewScrollTop);
                            if (start != HowItWorksSection.Top)
                                Assert.Equal(UiScale.Current.Px(16), host.PointToClient(heading.PointToScreen(Point.Empty)).Y);
                            // Close: the first focus and the default; its description is the start section.
                            (HowItWorksSection section, string title, string[] texts) = HowItWorksDialog.Sections().Single(x => x.Section == (start == HowItWorksSection.Top ? HowItWorksSection.WhatItDoes : start));
                            Assert.Same(d.CloseControl, d.ActiveControl);
                            Assert.Same(d.CloseControl, d.AcceptButton);
                            Assert.Equal(title + ". " + string.Join(" ", texts), d.CloseControl.AccessibleDescription);
                            d.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-C-20: every dialog: the role, the HC frame, the message

        private static IEnumerable<(string Name, Func<Form> Create, Func<Form, Control> Focus, string? Spoken)> EveryDialog(PreviewContext p)
        {
            BlockLists l = p.Lists;
            HostsStatus on = UiScenarios.Status(l, ProtectionState.On);
            yield return ("strict", () => new StrictDialog(p.Context, StrictDialogMode.Confirm, true), f => ((StrictDialog)f).UseStrictControl!,
                Strings.StrictConfirmTitle + " " + Strings.StrictIntro(l.StrictExtraCount) + " " + string.Join(" ", Strings.StrictEffects) + " " + Strings.StrictOutro + " " + Strings.ShieldDescription);
            yield return ("strict-info", () => new StrictDialog(p.Context, StrictDialogMode.Info, false), f => ((StrictDialog)f).CancelControl,
                Strings.StrictInfoTitle + ". " + Strings.StrictIntro(l.StrictExtraCount) + " " + string.Join(" ", Strings.StrictEffects) + " " + Strings.StrictOutro);
            yield return ("undo", () => new UndoDialog(on), f => ((UndoDialog)f).CancelControl, Strings.UndoTitle + " " + Strings.UndoBody(on.BlockedCount, false));
            foreach (ResultKind kind in new[] { ResultKind.FileLocked, ResultKind.AccessDenied, ResultKind.Reverted, ResultKind.Unexpected })
            {
                ResultKind k = kind;
                yield return ("error-" + k, () => new ErrorDialog(p.Context, k, true, () => "details"), f => ((ErrorDialog)f).RetryControl!,
                    Strings.ErrorTitle(k) + ". " + Strings.ErrorBody(k, true) + " " + Strings.ShieldDescription);
            }
            yield return ("error-window", () => new ErrorDialog(p.Context, new InvalidOperationException("x")), f => ((ErrorDialog)f).CloseControl,
                Strings.WindowErrorTitle + ". " + Strings.WindowErrorBody);
            yield return ("link-failed", () => new OpenFailedDialog(p.Context, Strings.ReleasesUrl, false), f => ((OpenFailedDialog)f).CloseControl,
                Strings.LinkFailedTitle + ". " + Strings.LinkFailedBody + " " + Strings.ReleasesUrl);
            yield return ("microsoft-lines", () => new MicrosoftLinesDialog(p.Context, UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside)),
                f => ((MicrosoftLinesDialog)f).CloseControl, Strings.MicrosoftLinesTitle + ". " + Strings.MicrosoftLinesIntro);
            yield return ("about", () => new AboutDialog(new AppController(p.Context, on)), f => ((AboutDialog)f).CloseControl,
                Strings.AppName + ". " + Strings.AboutVersion(AppInfo.Version) + ". " + Strings.AboutSummary);
            yield return ("license", () => new LicenseDialog(), f => ((LicenseDialog)f).Box.TextBox, null);
            yield return ("how-it-works", () => new HowItWorksDialog(p.Context), f => ((HowItWorksDialog)f).CloseControl, Strings.HowWhatHeading + ". " + Strings.HowWhatBody);
        }

        [Fact]
        public void Every_dialog_has_the_dialog_role_and_its_first_focus_carries_the_message()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach ((string name, Func<Form> create, Func<Form, Control> focus, string? spoken) in EveryDialog(p))
                    {
                        using (Form d = create())
                        {
                            Assert.Equal(AccessibleRole.Dialog, d.AccessibleRole);
                            Assert.False(d.ShowInTaskbar, name);
                            Assert.False(d.MinimizeBox, name);
                            Assert.False(d.MaximizeBox, name);
                            Control first = focus(d);
                            Assert.Same(first, d.ActiveControl);
                            if (spoken != null) Assert.Equal(spoken, first.AccessibleDescription);
                            else Assert.Equal(Strings.LicenseTitle, first.AccessibleName);
                            // The default button is the accent one; Cancel or Close is Esc.
                            if (d.AcceptButton is ThemedButton accept) Assert.Equal(ButtonStyle.Accent, accept.Style);
                            Assert.NotNull(d.CancelButton);
                        }
                    }
                    using (var fix = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Assert.Equal(AccessibleRole.Dialog, fix.AccessibleRole);
                        Assert.Same(fix.PreviewSearchBox.TextBox, fix.ActiveControl);
                    }
                });
            }
        }

        [Fact]
        public void Every_dialog_has_a_frame_in_high_contrast()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (Theme theme in new[] { Theme.ForHighContrast(HighContrastPalette.HcBlack), Theme.Light })
                        {
                            ThemeManager.Override(theme);
                            UiScale s = UiScale.Current;
                            int frame = theme.IsHighContrast ? s.Stroke(2) : 0;
                            foreach ((string name, Func<Form> create, Func<Form, Control> focus, string? spoken) in EveryDialog(p))
                            {
                                using (Form d = create())
                                {
                                    Sta.ShowOffScreen(d);
                                    Assert.Equal(new Padding(frame), d.Padding);
                                    // The frame's pixels along the client edge, the body's card color just inside it.
                                    using (var bmp = new Bitmap(d.Width, d.Height))
                                    {
                                        d.DrawToBitmap(bmp, new Rectangle(Point.Empty, d.Size));
                                        Point client = d.PointToScreen(Point.Empty);
                                        int x0 = client.X - d.Left, y0 = client.Y - d.Top;
                                        int midY = y0 + d.ClientSize.Height / 2;
                                        if (frame > 0)
                                        {
                                            Assert.Equal(Pixels.Rgb(theme.TextPrimary), Pixels.Rgb(bmp, x0, midY));
                                            Assert.Equal(Pixels.Rgb(theme.TextPrimary), Pixels.Rgb(bmp, x0 + frame - 1, midY));
                                        }
                                        Assert.Equal(Pixels.Rgb(theme.CardBackground), Pixels.Rgb(bmp, x0 + frame + 1, y0 + frame + s.Px(4)));
                                    }
                                    d.Close();
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
        }
    }
}
