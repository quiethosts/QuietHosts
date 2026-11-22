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
    /// <summary>The results of changes (spec 3.4) and the Repair command (3.5.1), as pure functions.</summary>
    public class ChangeResultTests
    {
        private static readonly BlockLists Lists = BlockLists.LoadEmbedded();

        private static LaunchResult Launch(ResultKind kind) =>
            new LaunchResult(kind, kind == ResultKind.Declined ? (int?)null : ExitCodes.FromResult(kind), null, new string('a', 64), new string('b', 64));

        private static ElevatedCommand CommandFor(ChangeKind kind)
        {
            switch (kind)
            {
                case ChangeKind.TurnOff:
                case ChangeKind.TurnOffForTest:
                    return ElevatedCommand.Off();
                case ChangeKind.Allow: return ElevatedCommand.Allow(Lists.DefaultEntries.Take(3).Select(e => e.Host));
                case ChangeKind.Block: return ElevatedCommand.Block(Lists.DefaultEntries.Take(1).Select(e => e.Host));
                case ChangeKind.Undo: return ElevatedCommand.Undo();
                default: return ElevatedCommand.On(Level.Strict);
            }
        }

        /// <summary>Every ChangeKind (as its number: the enum is internal) with every ResultKind.</summary>
        public static IEnumerable<object[]> Every() =>
            from ChangeKind k in Enum.GetValues(typeof(ChangeKind))
            from ResultKind r in Enum.GetValues(typeof(ResultKind))
            select new object[] { (int)k, r };

        // ---------------------------------------------------------------- T-B-16: ResultFor for every change and result

        [Theory]
        [MemberData(nameof(Every))]
        public void Every_change_and_result_has_its_bar(int change, ResultKind result)
        {
            var kind = (ChangeKind)change;
            ElevatedCommand cmd = CommandFor(kind);
            HostsStatus on = UiScenarios.Status(Lists, ProtectionState.On);
            HostsStatus refused = UiScenarios.Status(Lists, ProtectionState.Refused, refusal: RefusalReason.TooLarge);
            ChangeResult? r = AppController.ResultFor(kind, cmd, Launch(result), on, selfCheckPassed: true, Lists);
            switch (result)
            {
                case ResultKind.Success:
                    Assert.NotNull(r);
                    Assert.Equal(kind, r!.Kind);
                    Assert.Equal(result, r.Result);
                    Assert.Equal(ExpectedSuccess(kind), (r.Text, r.Severity));
                    Assert.False(r.OffersSelfCheckDetails);
                    break;
                case ResultKind.Declined:
                    Assert.Equal((Strings.ResultDeclined, NoticeSeverity.Info), (r!.Text, r.Severity));
                    break;
                case ResultKind.Busy:
                    Assert.Equal((Strings.ResultBusy, NoticeSeverity.Info), (r!.Text, r.Severity));
                    break;
                case ResultKind.Refused:
                    // R13: the file is fine, the change would make it too large; R12: the file itself is refused (no bar).
                    Assert.Equal((Strings.ResultRefusedChange, NoticeSeverity.Warning), (r!.Text, r.Severity));
                    Assert.Null(AppController.ResultFor(kind, cmd, Launch(result), refused, true, Lists));
                    break;
                default:
                    // R14: the error dialog shows these.
                    Assert.Null(r);
                    break;
            }
        }

        private static (string, NoticeSeverity) ExpectedSuccess(ChangeKind kind)
        {
            switch (kind)
            {
                case ChangeKind.TurnOn: return (Strings.ResultTurnOnOk, NoticeSeverity.Success);
                case ChangeKind.TurnOff:
                case ChangeKind.TurnOffForTest: return (Strings.ResultTurnOffOk, NoticeSeverity.Info);
                case ChangeKind.ChangeLevel: return (Strings.ResultLevelChanged(Level.Strict), NoticeSeverity.Success);
                case ChangeKind.Allow: return (Strings.ResultAllowed(3), NoticeSeverity.Success);
                case ChangeKind.Block: return (Strings.ResultBlocked(1), NoticeSeverity.Success);
                case ChangeKind.Undo: return (Strings.ResultUndoOk, NoticeSeverity.Info);
                case ChangeKind.UpdateList: return (Strings.ResultListUpdated(Lists.Version), NoticeSeverity.Success);
                case ChangeKind.Repair: return (Strings.ResultRepaired(true), NoticeSeverity.Success);
                case ChangeKind.RemoveOlderList: return (Strings.ResultOlderListRemoved, NoticeSeverity.Success);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        [Fact]
        public void The_self_check_and_the_repair_pick_their_own_texts()
        {
            HostsStatus on = UiScenarios.Status(Lists, ProtectionState.On);
            ChangeResult failed = AppController.ResultFor(ChangeKind.TurnOn, ElevatedCommand.On(Level.Standard), Launch(ResultKind.Success), on, selfCheckPassed: false, Lists)!;
            Assert.Equal((Strings.ResultSelfCheckFailed, NoticeSeverity.Warning, true), (failed.Text, failed.Severity, failed.OffersSelfCheckDetails));
            ChangeResult cleaned = AppController.ResultFor(ChangeKind.Repair, ElevatedCommand.Off(), Launch(ResultKind.Success), on, true, Lists)!;
            Assert.Equal("Your hosts file is repaired.", cleaned.Text);
            Assert.Equal("Your hosts file is repaired. Restart your browser to see the change.", Strings.ResultRepaired(true));
            Assert.Equal("You're now using Standard. Restart your browser to see the change.", Strings.ResultLevelChanged(Level.Standard));
            Assert.Equal("Nothing was changed. QuietHosts won't change your hosts file. It's larger than 16 MB, usually because another program added a very large list.",
                Strings.ResultRefusedAnnouncement(Strings.Headline(ProtectionState.Refused, RefusalReason.TooLarge), Strings.DetailTooLarge));
        }

        [Fact]
        public void Repair_rewrites_the_block_while_on_and_cleans_while_off()
        {
            ElevatedCommand on = AppController.RepairCommand(UiScenarios.Status(Lists, ProtectionState.On, Level.Strict))!;
            Assert.Equal((Verb.On, (Level?)Level.Strict), (on.Verb, on.Level));
            Assert.Equal(Verb.Off, AppController.RepairCommand(UiScenarios.Status(Lists, ProtectionState.Off))!.Verb);
            foreach (ProtectionState state in new[] { ProtectionState.Removed, ProtectionState.OlderList, ProtectionState.CantRead, ProtectionState.Refused })
                Assert.Null(AppController.RepairCommand(UiScenarios.Status(Lists, state)));
        }
    }

    /// <summary>The controller's session state: "off after a test", the result bar, closed notices and level choices (spec 3.11).</summary>
    [Collection(WindowCollection.Name)]
    public class AppControllerTests
    {
        private static Task Run(Func<Task> action)
        {
            // Outside a window: the awaits come back on worker threads, which is fine for these (no window listens).
            Task t = action();
            t.Wait(10000);
            return t;
        }

        // ---------------------------------------------------------------- T-B-09: off after a test

        [Fact]
        public void Off_for_a_test_lasts_until_another_change_or_a_status_that_isnt_off()
        {
            using (var p = new PreviewContext())
            {
                HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                HostsStatus next = off;
                var c = new AppController(p.Context, on, Flow.Returns(ResultKind.Success), () => next);
                Assert.False(c.OffForTest);

                // A plain Turn off doesn't set it.
                Run(() => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, null!));
                Assert.False(c.OffForTest);
                Assert.Equal(Strings.DetailOff, Strings.Detail(c.Status, c.OffForTest));

                // The Fix dialog's test turn-off sets it: the main window says why protection is off (S2).
                c.SetStatus(on);
                Run(() => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOffForTest, null!));
                Assert.True(c.OffForTest);
                Assert.Equal(Strings.DetailOffAfterTest, Strings.Detail(c.Status, c.OffForTest));
                Assert.Equal(Strings.ResultTurnOffOk, c.ResultLine);

                // A declined change leaves it; a status read that is still Off too.
                var declined = new AppController(p.Context, off, Flow.Returns(ResultKind.Declined), () => off);
                declined.PreviewOffForTest();
                Run(() => declined.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.TurnOn, null!));
                Assert.True(declined.OffForTest);

                // A successful Turn on clears it.
                next = on;
                Run(() => c.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.TurnOn, null!));
                Assert.False(c.OffForTest);

                // A status that isn't Off clears it.
                declined.SetStatus(UiScenarios.Status(p.Lists, ProtectionState.Removed));
                Assert.False(declined.OffForTest);
            }
        }

        [Fact]
        public void The_result_bar_and_its_events()
        {
            using (var p = new PreviewContext())
            {
                HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                var c = new AppController(p.Context, on, Flow.Returns(ResultKind.Success), () => on);
                var finished = new List<ChangeFinishedEventArgs>();
                int statusChanges = 0;
                c.ChangeFinished += (s, e) => finished.Add(e);
                c.StatusChanged += (s, e) => statusChanges++;
                Run(() => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, null!));
                ChangeFinishedEventArgs e1 = Assert.Single(finished);
                Assert.Equal((ChangeKind.TurnOff, ResultKind.Success, Strings.ResultTurnOffOk, NoticeSeverity.Info), (e1.Kind, e1.Result, e1.Line, e1.Severity));
                Assert.Equal(Strings.ResultTurnOffOk, c.StatusLineText);

                // Closing the bar: gone everywhere (StatusChanged), nothing else changes.
                int before = statusChanges;
                c.CloseResult();
                Assert.Null(c.LastResult);
                Assert.Null(c.ResultLine);
                Assert.Equal(before + 1, statusChanges);
                c.CloseResult();
                Assert.Equal(before + 1, statusChanges);
                Assert.Same(on, c.Status);

                // The pending level: kept by a Repair that only cleans, cleared by one that rewrites the block.
                using (var dir = new TempDir())
                {
                    AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                    var ctx = new UiContext(env, p.Lists, p.Context.ExePath, RunLocationTips.None, false, preview: false);
                    HostsStatus pending = UiScenarios.Status(p.Lists, ProtectionState.Off, pendingLevel: Level.Strict);
                    env.User.SetPendingLevel(Level.Strict);
                    var real = new AppController(ctx, pending, Flow.Returns(ResultKind.Success), () => pending);
                    Run(() => real.RunAsync(ElevatedCommand.Off(), ChangeKind.Repair, null!));
                    Assert.Equal(Level.Strict, env.User.Read().PendingLevel);
                    real.SetStatus(pending);
                    Run(() => real.RunAsync(ElevatedCommand.On(Level.Strict), ChangeKind.Repair, null!));
                    Assert.Null(env.User.Read().PendingLevel);
                    env.User.SetPendingLevel(Level.Strict);
                    real.SetStatus(pending);
                    Run(() => real.RunAsync(ElevatedCommand.On(Level.Strict), ChangeKind.RemoveOlderList, null!));
                    Assert.Null(env.User.Read().PendingLevel);
                }
            }
        }

        [Fact]
        public void A_level_is_applied_only_when_it_can_and_needs_to_be()
        {
            using (var p = new PreviewContext())
            {
                var commands = new List<ElevatedCommand>();
                HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                var c = new AppController(p.Context, on, Flow.Returns(ResultKind.Declined, commands), () => on);
                // The level already in use, Can't read, and another run holding the mutex: nothing happens.
                Run(() => c.ApplyLevelAsync(Level.Standard, null!));
                var cantRead = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.CantRead), Flow.Returns(ResultKind.Declined, commands));
                Run(() => cantRead.ApplyLevelAsync(Level.Strict, null!));
                var otherRun = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, runInProgress: true), Flow.Returns(ResultKind.Declined, commands));
                Run(() => otherRun.ApplyLevelAsync(Level.Strict, null!));
                Assert.Empty(commands);
                // On: one prompt.
                Task t = Run(() => c.ApplyLevelAsync(Level.Strict, null!));
                Assert.True(t.IsCompleted);
                Assert.Equal(Level.Strict, Assert.Single(commands).Level);
                Assert.Equal(ChangeKind.ChangeLevel, c.LastResult!.Kind);
            }
        }
    }
}
