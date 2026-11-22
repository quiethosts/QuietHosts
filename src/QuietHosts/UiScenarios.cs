#if DEBUG || QUIETHOSTS_UI_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>One window in one state, built from fake data.</summary>
    internal sealed class UiScenario
    {
        public UiScenario(string name, Func<Form> create, Action<Form>? afterShown = null)
        {
            Name = name;
            Create = create;
            AfterShown = afterShown;
        }

        /// <summary>The PNG name for --render-ui.</summary>
        public string Name { get; }
        public Func<Form> Create { get; }
        /// <summary>Runs once the window is shown (search, selection).</summary>
        public Action<Form>? AfterShown { get; }
    }

    /// <summary>
    /// Every main-window state, warning and tip, the first-run panel and each dialog, with fake statuses. Debug's
    /// --render-ui draws them to PNG files; the UI tests construct them. Nothing here reads the real hosts file.
    /// </summary>
    internal static class UiScenarios
    {
        public const string FakeLastRun =
            "time: 2026-11-20T09:30:00.000Z\napp: 1.0.0\ncommand: on --level strict\nresult: Unexpected (exit 15)\nlists: 2026-11-20\nlog: example log line\n";

        public const string OlderListVersion = "2026-01-15";
        public const int FakeLegacyCount = 412;
        public const int FakeLargeOutside = 1_480_000;

        /// <summary>A screen too short for the windows even at the normal text size (the content then scrolls).</summary>
        public static readonly System.Drawing.Rectangle ShortScreen = new System.Drawing.Rectangle(0, 0, 1366, 420);

        /// <summary>The working area of a 1366 x 768 laptop: too short for the Fix dialog at 150% text.</summary>
        public static readonly System.Drawing.Rectangle LaptopScreen = new System.Drawing.Rectangle(0, 0, 1366, 720);

        /// <summary>
        /// So short that the redesigned main window's main button is below the first screenful, while its headline still fits
        /// (epx; <see cref="Scaled"/> gives it in pixels).
        /// </summary>
        public static readonly System.Drawing.Rectangle TinyScreen = new System.Drawing.Rectangle(0, 0, 1366, 250);

        /// <summary>A 1920 x 1080 screen at 150% scale, the most common laptop setup: a 1280 x 672 epx working area.</summary>
        public static readonly System.Drawing.Rectangle LaptopScreen150 = new System.Drawing.Rectangle(0, 0, 1280, 672);

        /// <summary>Made-up lines another program might add that block Microsoft addresses (the MicrosoftLinesOutside warning).</summary>
        public static readonly string[] FakeMicrosoftLines =
        {
            "0.0.0.0 vortex.data.microsoft.com",
            "0.0.0.0 settings-win.data.microsoft.com",
            "0.0.0.0 watson.telemetry.microsoft.com",
        };

        /// <summary>A stand-in for the block's line fingerprint (an On status).</summary>
        public static readonly string FakeBlockLinesSha256 = new string('c', 64);

        /// <summary>A list version two months newer than this exe's: a block written by a newer QuietHosts.exe.</summary>
        public static string NewerListVersion(BlockLists lists)
        {
            DateTime d = DateTime.ParseExact(lists.Version, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return d.AddMonths(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A fake status as StatusReader would report it.</summary>
        /// <param name="blockedHosts">On: the addresses in the block; by default what the level blocks minus the allowed ones.</param>
        /// <param name="runInProgress">Another QuietHosts run holds the write mutex.</param>
        /// <param name="microsoftLines">
        /// The lines that block Microsoft addresses (all of them: the status keeps the first 50, cut to 200 characters, and
        /// counts and hashes them all); by default <see cref="FakeMicrosoftLines"/> when the warnings include
        /// MicrosoftLinesOutside, else none.
        /// </param>
        /// <param name="blockLinesSha256">On: the block's line fingerprint (default <see cref="FakeBlockLinesSha256"/>).</param>
        /// <param name="pendingLevel">Not On: the level chosen while off (it becomes the selected level).</param>
        public static HostsStatus Status(BlockLists lists, ProtectionState state, Level level = Level.Standard, int allowed = 0,
            HostsWarnings warnings = HostsWarnings.None, RefusalReason refusal = RefusalReason.None, bool firstRunDone = true,
            bool legacy = false, string? blockListVersion = null, IEnumerable<string>? blockedHosts = null, bool runInProgress = false,
            IEnumerable<string>? microsoftLines = null, string? blockLinesSha256 = null, Level? pendingLevel = null)
        {
            bool on = state == ProtectionState.On;
            bool hasLegacy = legacy || state == ProtectionState.OlderList;
            List<string> allowlist = lists.ForLevel(level).Reverse().Take(allowed).Select(e => e.Host).OrderBy(h => h, StringComparer.Ordinal).ToList();
            var block = new HashSet<string>(blockedHosts ?? lists.ForLevel(level).Select(e => e.Host).Except(allowlist, StringComparer.Ordinal), StringComparer.Ordinal);
            string folder = HostsDatabase.DefaultFolder;
            // Can't read and Refused carry no lines or fingerprints, as StatusReader reports them.
            bool readable = state != ProtectionState.CantRead && state != ProtectionState.Refused;
            List<string> msLines = readable
                ? (microsoftLines ?? ((warnings & HostsWarnings.MicrosoftLinesOutside) != 0 ? FakeMicrosoftLines : Enumerable.Empty<string>())).ToList()
                : new List<string>();
            return new HostsStatus
            {
                State = state,
                Refusal = state == ProtectionState.Refused ? refusal : RefusalReason.None,
                Warnings = warnings,
                HostsPath = Path.Combine(folder, "hosts"),
                HostsFolder = folder,
                MovedFolder = refusal == RefusalReason.HostsPathMoved ? @"D:\Tools\hosts-files" : null,
                ReadError = state == ProtectionState.CantRead ? new IOException("The process cannot access the file because it is being used by another process.") : null,
                FileExists = true,
                FileSize = 32_000,
                BytesOutsideBlocks = (warnings & HostsWarnings.LargeFile) != 0 ? FakeLargeOutside : 824,
                FileSha256 = new string('a', 64),
                BlockedCount = on ? block.Count : 0,
                BlockLevel = on ? level : (Level?)null,
                BlockListVersion = on ? blockListVersion ?? lists.Version : null,
                BlockedHosts = on ? block : (IReadOnlyCollection<string>)Array.Empty<string>(),
                FirstBlockedHost = on ? block.OrderBy(h => h, StringComparer.Ordinal).FirstOrDefault() : null,
                LegacyCount = hasLegacy ? FakeLegacyCount : 0,
                HasLegacyBlock = hasLegacy,
                SavedEnabled = on || state == ProtectionState.Removed,
                SavedLevel = level,
                PendingLevel = on ? null : pendingLevel,
                SelectedLevel = on ? level : pendingLevel ?? level,
                Allowlist = allowlist,
                AllowedInLevelCount = allowlist.Count,
                // As StatusReader says it: a block, the older block, damaged markers or saved settings (a change that didn't
                // finish left its checksums in the per-PC store).
                CanUndo = state != ProtectionState.Off || hasLegacy || allowed > 0 ||
                    (warnings & (HostsWarnings.MalformedMarkers | HostsWarnings.ChangeNotFinished)) != 0,
                FirstRunDone = firstRunDone,
                LastRun = FakeLastRun,
                RunInProgress = runInProgress,
                BlockLinesSha256 = on ? blockLinesSha256 ?? FakeBlockLinesSha256 : null,
                MicrosoftLines = msLines.Take(HostsDocument.MaxMicrosoftLinesShown)
                    .Select(l => l.Length <= HostsDocument.MaxMicrosoftLineLength ? l : l.Substring(0, HostsDocument.MaxMicrosoftLineLength)).ToList(),
                MicrosoftLineCount = msLines.Count,
                MicrosoftLinesSha256 = msLines.Count > 0 ? Hashing.Sha256Hex(Latin1.Encode(string.Join("\n", msLines))) : null,
            };
        }

        /// <summary>
        /// A pretend screen given in epx (<see cref="ShortScreen"/>, <see cref="LaptopScreen150"/>, ...) in pixels at the current
        /// DPI, so a 1920 x 1080 screen at 150% is the same screen in a 200% render.
        /// </summary>
        public static System.Drawing.Rectangle Scaled(System.Drawing.Rectangle epx)
        {
            UiScale s = UiScale.Current;
            return new System.Drawing.Rectangle(s.Px(epx.X), s.Px(epx.Y), s.Px(epx.Width), s.Px(epx.Height));
        }

        /// <summary>The result bar of a finished change, as <see cref="AppController.ResultFor"/> makes it.</summary>
        public static ChangeResult Result(BlockLists lists, ChangeKind kind, ElevatedCommand command, ResultKind result, HostsStatus after, bool selfCheckPassed = true)
        {
            var launch = new LaunchResult(result, result == ResultKind.Declined ? (int?)null : ExitCodes.FromResult(result), null, new string('a', 64), new string('b', 64));
            return AppController.ResultFor(kind, command, launch, after, selfCheckPassed, lists)
                ?? throw new InvalidOperationException("That change has no result bar.");
        }

        /// <summary>Made-up lines that block Microsoft addresses, more than the status keeps (50).</summary>
        public static IEnumerable<string> ManyMicrosoftLines(int count) =>
            Enumerable.Range(1, count).Select(i => "0.0.0.0 example" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ".telemetry.microsoft.com");

        public static List<UiScenario> All(UiContext ctx)
        {
            BlockLists l = ctx.Lists;
            var withTips = new UiContext(ctx.Env, l, ctx.ExePath, RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp, false, true);
            // A neutral made-up signer: published source must not suggest who signs future releases.
            var signed = new UiContext(ctx.Env, l, ctx.ExePath, RunLocationTips.None, false, true, new SignatureState(SignatureKind.Signed, "Example Publisher"));
            var unverified = new UiContext(ctx.Env, l, ctx.ExePath, RunLocationTips.None, false, true, new SignatureState(SignatureKind.Unverified, null));
            HostsWarnings everyWarning = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
                HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;
            HostsStatus on = Status(l, ProtectionState.On);
            HostsStatus strictOn = Status(l, ProtectionState.On, Level.Strict);
            HostsStatus strictAllowed = Status(l, ProtectionState.On, Level.Strict, allowed: 3);
            HostsStatus off = Status(l, ProtectionState.Off);
            HostsStatus warnings = Status(l, ProtectionState.On, warnings: everyWarning, legacy: true, blockListVersion: OlderListVersion);
            ElevatedCommand turnOn = ElevatedCommand.On(Level.Standard);
            ElevatedCommand allowTwo = ElevatedCommand.Allow(l.DefaultEntries.Take(2).Select(e => e.Host));

            var list = new List<UiScenario>
            {
                MainWindow(ctx, "main-off", off),
                MainWindow(ctx, "main-off-after-test", off, (c, f) => c.PreviewOffForTest()),
                MainWindow(ctx, "main-off-strict-selected", Status(l, ProtectionState.Off, pendingLevel: Level.Strict)),
                MainWindow(ctx, "main-on-standard", on),
                MainWindow(ctx, "main-on-strict-allowed", strictAllowed),
                MainWindow(ctx, "main-removed", Status(l, ProtectionState.Removed, warnings: HostsWarnings.LegacyBlockToo, legacy: true)),
                MainWindow(ctx, "main-older-list", Status(l, ProtectionState.OlderList)),
                MainWindow(ctx, "main-cant-read", Status(l, ProtectionState.CantRead)),
                MainWindow(ctx, "main-refused-moved", Status(l, ProtectionState.Refused, refusal: RefusalReason.HostsPathMoved)),
                MainWindow(ctx, "main-refused-encoding", Status(l, ProtectionState.Refused, refusal: RefusalReason.UnsupportedEncoding)),
                MainWindow(ctx, "main-refused-too-large", Status(l, ProtectionState.Refused, refusal: RefusalReason.TooLarge)),
                MainWindow(ctx, "main-unknown-state", Status(l, ProtectionState.Refused, refusal: RefusalReason.None)),
                MainWindow(withTips, "main-warnings-and-tips", warnings),
                MainWindow(withTips, "main-notices-expanded", warnings, afterShown: f => f.PreviewExpandNotices()),
                MainWindow(ctx, "main-notice-repair-off", Status(l, ProtectionState.Off, warnings: HostsWarnings.ChangeNotFinished | HostsWarnings.MalformedMarkers)),
                MainWindow(withTips, "main-short-screen", warnings, (c, f) => f.PreviewWorkingArea = Scaled(ShortScreen)),
                MainWindow(ctx, "main-laptop-after-change", on, (c, f) =>
                {
                    f.PreviewWorkingArea = Scaled(LaptopScreen150);
                    c.PreviewResult(Result(l, ChangeKind.TurnOn, turnOn, ResultKind.Success, on), f);
                }),
                MainWindow(ctx, "main-one-fix-action", Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.LegacyBlockToo, legacy: true,
                    blockListVersion: OlderListVersion)),
                MainWindow(ctx, "main-newer-list", Status(l, ProtectionState.On, blockListVersion: OlderListVersion)),
                MainWindow(ctx, "main-older-app", Status(l, ProtectionState.On, blockListVersion: NewerListVersion(l))),
                MainWindow(ctx, "main-waiting", on, (c, f) => c.PreviewBusy(Strings.WaitingForPermission)),
                MainWindow(ctx, "main-changing", on, (c, f) => c.PreviewBusy(Strings.ChangingHostsFile)),
                MainWindow(ctx, "main-changing-slow", on, (c, f) => c.PreviewBusy(Strings.ChangingHostsFile, takingLong: true)),
                MainWindow(ctx, "main-animations-off", on, (c, f) =>
                {
                    c.PreviewBusy(Strings.ChangingHostsFile);
                    f.PreviewAnimationsOff();
                }),
                MainWindow(ctx, "main-other-change-running", Status(l, ProtectionState.On, runInProgress: true)),
                MainWindow(ctx, "main-other-change-running-off", Status(l, ProtectionState.Off, runInProgress: true)),
                MainWindow(ctx, "main-result-turn-on", on, (c, f) => c.PreviewResult(Result(l, ChangeKind.TurnOn, turnOn, ResultKind.Success, on), f)),
                MainWindow(ctx, "main-result-self-check-failed", on, (c, f) => c.PreviewSelfCheckFailed(new SelfCheckResult(false, "no A record in the DNS cache"), f)),
                MainWindow(ctx, "main-result-self-check-copied", on, (c, f) => c.PreviewSelfCheckFailed(new SelfCheckResult(false, "no A record in the DNS cache"), f),
                    afterShown: f => f.PreviewCopySelfCheckDetails()),
                MainWindow(ctx, "main-result-list-updated", on, (c, f) => c.PreviewResult(Result(l, ChangeKind.UpdateList, turnOn, ResultKind.Success, on), f)),
                MainWindow(ctx, "main-result-declined", off, (c, f) => c.PreviewResult(Result(l, ChangeKind.TurnOn, turnOn, ResultKind.Declined, off), f)),
                MainWindow(ctx, "main-result-undo", off, (c, f) => c.PreviewResult(Result(l, ChangeKind.Undo, ElevatedCommand.Undo(), ResultKind.Success, off), f)),
                MainWindow(ctx, "main-result-turn-off", off, (c, f) => c.PreviewResult(Result(l, ChangeKind.TurnOff, ElevatedCommand.Off(), ResultKind.Success, off), f)),
                MainWindow(ctx, "main-result-level-changed", strictOn, (c, f) => c.PreviewResult(Result(l, ChangeKind.ChangeLevel, ElevatedCommand.On(Level.Strict), ResultKind.Success, strictOn), f)),
                MainWindow(ctx, "main-result-allowed", strictAllowed, (c, f) => c.PreviewResult(Result(l, ChangeKind.Allow, allowTwo, ResultKind.Success, strictAllowed), f)),
                MainWindow(ctx, "main-result-repaired", on, (c, f) => c.PreviewResult(Result(l, ChangeKind.Repair, turnOn, ResultKind.Success, on), f)),
                MainWindow(ctx, "main-result-older-list-removed", on, (c, f) => c.PreviewResult(Result(l, ChangeKind.RemoveOlderList, turnOn, ResultKind.Success, on), f)),
                MainWindow(ctx, "main-result-busy", on, (c, f) => c.PreviewResult(Result(l, ChangeKind.TurnOff, ElevatedCommand.Off(), ResultKind.Busy, on), f)),
                MainWindow(ctx, "main-result-refused-change", strictAllowed, (c, f) => c.PreviewResult(Result(l, ChangeKind.ChangeLevel, ElevatedCommand.On(Level.Strict), ResultKind.Refused, strictAllowed), f)),
                // R12: the change was refused and so is the file: no bar, the status card explains it.
                MainWindow(ctx, "main-result-refused", Status(l, ProtectionState.Refused, refusal: RefusalReason.TooLarge)),
                MainWindow(ctx, "main-focus", Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock),
                    (c, f) => UiOverrides.Force(f.PreviewStatusCard.MainButton, ForcedState.Focused)),
                MainWindow(ctx, "first-run", Status(l, ProtectionState.Off, firstRunDone: false), firstRun: true),
                MainWindow(ctx, "first-run-older-list", Status(l, ProtectionState.OlderList, firstRunDone: false), firstRun: true),
                MainWindow(ctx, "first-run-short-screen", Status(l, ProtectionState.Off, firstRunDone: false), (c, f) => f.PreviewWorkingArea = Scaled(ShortScreen), firstRun: true),
                MainWindow(signed, "first-run-signed", Status(l, ProtectionState.Off, firstRunDone: false), firstRun: true),

                new UiScenario("microsoft-lines", () => new MicrosoftLinesDialog(ctx, Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside))),
                new UiScenario("microsoft-lines-many", () => new MicrosoftLinesDialog(ctx,
                    Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside, microsoftLines: ManyMicrosoftLines(60)))),

                new UiScenario("strict-confirm", () => new StrictDialog(ctx, StrictDialogMode.Confirm, elevates: true)),
                new UiScenario("strict-confirm-off", () => new StrictDialog(ctx, StrictDialogMode.Confirm, elevates: false)),
                new UiScenario("strict-info", () => new StrictDialog(ctx, StrictDialogMode.Info, elevates: false)),

                Fix(ctx, "fix-empty", on),
                Fix(ctx, "fix-strict", strictOn),
                Fix(ctx, "fix-searched", on, afterShown: f => f.PreviewSearch("yahoo")),
                Fix(ctx, "fix-row-details", on, afterShown: f => { f.PreviewSearch(NoteHost); f.PreviewSelectHost(NoteHost); }),
                Fix(ctx, "fix-row-no-effects", on, afterShown: f => { f.PreviewSearch(NoNoteHost(l)); f.PreviewSelectHost(NoNoteHost(l)); }),
                Fix(ctx, "fix-allowed", strictAllowed),
                Fix(ctx, "fix-allowed-selected", strictAllowed, afterShown: f => f.PreviewSelectAllowed(2)),
                Fix(ctx, "fix-filter-allowed", strictAllowed, afterShown: f => f.PreviewFilterAllowed()),
                Fix(ctx, "fix-no-match", on, afterShown: f => f.PreviewSearch("example-shop")),
                Fix(ctx, "fix-over-100", on, afterShown: f => f.PreviewSelect(Limits.MaxHostsPerRun + 20)),
                Fix(ctx, "fix-off", off),
                Fix(ctx, "fix-off-after-test", off, (c, d) => c.PreviewOffForTest()),
                Fix(ctx, "fix-removed", Status(l, ProtectionState.Removed)),
                Fix(ctx, "fix-older-list", Status(l, ProtectionState.OlderList)),
                Fix(ctx, "fix-cant-read", Status(l, ProtectionState.CantRead)),
                Fix(ctx, "fix-laptop-screen", on, (c, d) => d.PreviewWorkingArea = Scaled(LaptopScreen)),
                Fix(ctx, "fix-short-screen", on, (c, d) => d.PreviewWorkingArea = Scaled(ShortScreen)),
                Fix(ctx, "fix-min-size", on, afterShown: f => f.PreviewMinimumSize()),
                Fix(ctx, "fix-result", strictAllowed, (c, d) => c.PreviewResult(Result(l, ChangeKind.Allow, allowTwo, ResultKind.Success, strictAllowed), d)),
                Fix(ctx, "fix-result-declined", on, (c, d) => c.PreviewResult(Result(l, ChangeKind.TurnOffForTest, ElevatedCommand.Off(), ResultKind.Declined, on), d)),
                Fix(ctx, "fix-changing-slow", strictAllowed, (c, d) => c.PreviewBusy(Strings.ChangingHostsFile, takingLong: true)),
                Fix(ctx, "fix-other-change-running", Status(l, ProtectionState.On, Level.Strict, allowed: 3, runInProgress: true), afterShown: f => f.PreviewSelect(1)),

                new UiScenario("undo", () => new UndoDialog(on)),
                new UiScenario("undo-older-list", () => new UndoDialog(Status(l, ProtectionState.OlderList))),
                new UiScenario("undo-off", () => new UndoDialog(Status(l, ProtectionState.Off, allowed: 2))),

                Error(ctx, "error-access-denied", ResultKind.AccessDenied, false),
                Error(ctx, "error-file-locked", ResultKind.FileLocked, false),
                Error(ctx, "error-reverted", ResultKind.Reverted, true),
                Error(ctx, "error-unexpected-unchanged", ResultKind.Unexpected, false),
                Error(ctx, "error-unexpected-incomplete", ResultKind.Unexpected, true),
                new UiScenario("error-window", () => new ErrorDialog(ctx, new InvalidOperationException("Example problem in the window."))),
                Error(ctx, "error-copied", ResultKind.FileLocked, false, d => d.PreviewCopyDetails()),

                // One of the longer addresses, so the render shows it wrapping in its box (6.5).
                new UiScenario("link-failed", () => new OpenFailedDialog(ctx, Strings.ReportSiteUrl, isFolder: false)),
                new UiScenario("folder-failed", () => new OpenFailedDialog(ctx, HostsDatabase.DefaultFolder, isFolder: true)),

                new UiScenario("about", () => new AboutDialog(new AppController(ctx, on))),
                new UiScenario("about-signed", () => new AboutDialog(new AppController(signed, on))),
                new UiScenario("about-unverified", () => new AboutDialog(new AppController(unverified, on))),
                new UiScenario("license", () => new LicenseDialog()),
                new UiScenario("how-it-works", () => new HowItWorksDialog(ctx)),
                // On a laptop screen the content scrolls, so the start section's heading shows at the top.
                new UiScenario("how-it-works-own-folder", () => new HowItWorksDialog(ctx, HowItWorksSection.OwnFolder) { PreviewWorkingArea = Scaled(LaptopScreen) }),
                new UiScenario("how-it-works-short-screen", () => new HowItWorksDialog(ctx) { PreviewWorkingArea = Scaled(ShortScreen) }),

                new UiScenario("gallery", () => new GalleryForm(ctx)),
            };
            return list;
        }

        private static UiScenario MainWindow(UiContext ctx, string name, HostsStatus status, Action<AppController, MainForm>? setup = null, bool firstRun = false,
            Action<MainForm>? afterShown = null)
        {
            return new UiScenario(name, () =>
            {
                var c = new AppController(ctx, status);
                var f = new MainForm(c, firstRun);
                setup?.Invoke(c, f);
                return f;
            }, afterShown == null ? (Action<Form>?)null : f => afterShown((MainForm)f));
        }

        /// <summary>The Fix dialog's row with a known effect (the caution glyph in the details line).</summary>
        public const string NoteHost = "pagead2.googlesyndication.com";

        /// <summary>A Standard address whose note is empty ("None known").</summary>
        public static string NoNoteHost(BlockLists lists) => lists.DefaultEntries.First(e => e.Note.Length == 0).Host;

        /// <param name="setup">Runs once the dialog exists, before it shows (a session state, a pretend screen, a result).</param>
        private static UiScenario Fix(UiContext ctx, string name, HostsStatus status, Action<AppController, FixDialog>? setup = null, Action<FixDialog>? afterShown = null)
        {
            return new UiScenario(name, () =>
            {
                var c = new AppController(ctx, status);
                var d = new FixDialog(c);
                setup?.Invoke(c, d);
                return d;
            }, afterShown == null ? (Action<Form>?)null : f => afterShown((FixDialog)f));
        }

        private static UiScenario Error(UiContext ctx, string name, ResultKind kind, bool changed, Action<ErrorDialog>? afterShown = null) =>
            new UiScenario(name, () => new ErrorDialog(ctx, kind, changed, () => DetailsReport.Build(ctx, ElevatedCommand.On(Level.Strict),
                new LaunchResult(kind, ExitCodes.FromResult(kind), null, new string('a', 64), new string(changed ? 'b' : 'a', 64)), null, null)),
                afterShown == null ? (Action<Form>?)null : f => afterShown((ErrorDialog)f));
    }
}
#endif
