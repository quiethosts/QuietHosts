using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    public class LauncherTests
    {
        private sealed class FakeRunner : IProcessRunner
        {
            public Func<ProcessStartInfo, int> Behavior { get; set; } = _ => 0;
            public ProcessStartInfo? Last { get; private set; }
            public int ThreadId { get; private set; }

            public int RunAndWait(ProcessStartInfo startInfo, Action started)
            {
                Last = startInfo;
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                int code = Behavior(startInfo);   // throws here when declined, before "started"
                started();
                return code;
            }
        }

        private sealed class SyncProgress : IProgress<LaunchStage>
        {
            public List<LaunchStage> Stages { get; } = new List<LaunchStage>();
            public void Report(LaunchStage value) => Stages.Add(value);
        }

        [Theory]
        [InlineData(0, ResultKind.Success)]
        [InlineData(10, ResultKind.AccessDenied)]
        [InlineData(11, ResultKind.FileLocked)]
        [InlineData(12, ResultKind.Reverted)]
        [InlineData(13, ResultKind.Busy)]
        [InlineData(14, ResultKind.Unexpected)]
        [InlineData(15, ResultKind.Unexpected)]
        [InlineData(16, ResultKind.Refused)]
        [InlineData(-1073741819, ResultKind.Unexpected)]
        public void Exit_codes_map_to_results(int exitCode, ResultKind expected)
        {
            using (var t = new TestEnv())
            {
                var runner = new FakeRunner { Behavior = _ => exitCode };
                LaunchResult r = new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe", runner).Run(ElevatedCommand.Off());
                Assert.Equal(expected, r.Kind);
                Assert.Equal(exitCode, r.ExitCode);
            }
        }

        [Fact]
        public void Error_1223_means_Declined()
        {
            using (var t = new TestEnv())
            {
                var runner = new FakeRunner { Behavior = _ => throw new Win32Exception(1223) };
                var progress = new SyncProgress();
                LaunchResult r = new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe", runner).Run(ElevatedCommand.Undo(), progress);
                Assert.Equal(ResultKind.Declined, r.Kind);
                Assert.Null(r.ExitCode);
                Assert.Equal(new[] { LaunchStage.WaitingForPermission }, progress.Stages);
            }
        }

        [Fact]
        public void Other_start_failures_are_Unexpected()
        {
            using (var t = new TestEnv())
            {
                var runner = new FakeRunner { Behavior = _ => throw new Win32Exception(2) };
                LaunchResult r = new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe", runner).Run(ElevatedCommand.Off());
                Assert.Equal(ResultKind.Unexpected, r.Kind);
                Assert.IsType<Win32Exception>(r.Error);
            }
        }

        [Fact]
        public void Progress_reports_waiting_then_changing()
        {
            using (var t = new TestEnv())
            {
                var progress = new SyncProgress();
                new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe", new FakeRunner()).Run(ElevatedCommand.On(Level.Strict), progress);
                Assert.Equal(new[] { LaunchStage.WaitingForPermission, LaunchStage.Changing }, progress.Stages);
            }
        }

        [Fact]
        public void The_hosts_hash_before_and_after_tells_the_two_Unexpected_forms_apart()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                var unchanged = new ElevatedLauncher(t.Env, "QuietHosts.exe", new FakeRunner { Behavior = _ => 15 }).Run(ElevatedCommand.Off());
                Assert.False(unchanged.HostsFileChanged);
                Assert.Equal(Hashing.Sha256Hex(t.ReadHosts()), unchanged.HostsSha256Before);

                var changed = new ElevatedLauncher(t.Env, "QuietHosts.exe", new FakeRunner
                {
                    Behavior = _ => { File.WriteAllText(t.HostsPath, "torn"); return 15; },
                }).Run(ElevatedCommand.Off());
                Assert.True(changed.HostsFileChanged);

                for (int i = 0; i < 20; i++) t.Files.ReadFaults.Enqueue(Faults.Denied());
                var unknown = new ElevatedLauncher(t.Env, "QuietHosts.exe", new FakeRunner { Behavior = _ => 15 }).Run(ElevatedCommand.Off());
                Assert.Null(unknown.HostsFileChanged);
            }
        }

        /// <summary>Calls "started" at once, then waits until the test lets the run finish.</summary>
        private sealed class SlowRunner : IProcessRunner
        {
            private readonly Func<bool> _wait;
            public SlowRunner(Func<bool> wait) { _wait = wait; }
            public int RunAndWait(ProcessStartInfo startInfo, Action started)
            {
                started();
                return _wait() ? 0 : 15;
            }
        }

        private sealed class SignalingProgress : IProgress<LaunchStage>
        {
            public List<LaunchStage> Stages { get; } = new List<LaunchStage>();
            public ManualResetEventSlim TakingLong { get; } = new ManualResetEventSlim();
            public void Report(LaunchStage value)
            {
                lock (Stages) Stages.Add(value);
                if (value == LaunchStage.TakingLong) TakingLong.Set();
            }
        }

        [Fact]
        public void A_run_still_going_after_the_wait_reports_TakingLong_once_and_is_still_waited_for()
        {
            using (var t = new TestEnv())
            {
                var progress = new SignalingProgress();
                // The run ends only once TakingLong arrived: the launcher must still be waiting for it, and its exit code counts.
                var runner = new SlowRunner(() => progress.TakingLong.Wait(TimeSpan.FromSeconds(20)));
                LaunchResult r = new ElevatedLauncher(t.Env, "QuietHosts.exe", runner, TimeSpan.FromMilliseconds(50)).Run(ElevatedCommand.Off(), progress);
                Assert.Equal(ResultKind.Success, r.Kind);
                Assert.Equal(0, r.ExitCode);
                Thread.Sleep(150);   // a second report would have come by now
                lock (progress.Stages)
                    Assert.Equal(new[] { LaunchStage.WaitingForPermission, LaunchStage.Changing, LaunchStage.TakingLong }, progress.Stages);
            }
        }

        [Fact]
        public void TakingLong_never_comes_after_the_result()
        {
            using (var t = new TestEnv())
            {
                // The time is up just as the run ends: the report comes before Run returns, or not at all.
                var runs = new List<(SignalingProgress Progress, int CountAtReturn)>();
                for (int i = 0; i < 40; i++)
                {
                    var progress = new SignalingProgress();
                    new ElevatedLauncher(t.Env, "QuietHosts.exe", new SlowRunner(() => true), TimeSpan.FromMilliseconds(i % 4)).Run(ElevatedCommand.Off(), progress);
                    lock (progress.Stages) runs.Add((progress, progress.Stages.Count));
                }
                Thread.Sleep(100);
                foreach (var run in runs)
                    lock (run.Progress.Stages) Assert.Equal(run.CountAtReturn, run.Progress.Stages.Count);
            }
        }

        [Fact]
        public void A_quick_run_or_a_declined_prompt_never_reports_TakingLong()
        {
            using (var t = new TestEnv())
            {
                var quick = new SyncProgress();
                new ElevatedLauncher(t.Env, "QuietHosts.exe", new FakeRunner(), TimeSpan.FromMilliseconds(300)).Run(ElevatedCommand.Off(), quick);
                var declined = new SyncProgress();
                new ElevatedLauncher(t.Env, "QuietHosts.exe", new FakeRunner { Behavior = _ => throw new Win32Exception(1223) }, TimeSpan.Zero)
                    .Run(ElevatedCommand.Off(), declined);
                Thread.Sleep(700);   // past the 300 ms: a timer left running would have reported by now
                Assert.Equal(new[] { LaunchStage.WaitingForPermission, LaunchStage.Changing }, quick.Stages);
                Assert.Equal(new[] { LaunchStage.WaitingForPermission }, declined.Stages);
                Assert.Equal(30, ElevatedLauncher.TakingLongAfter.TotalSeconds);
            }
        }

        [Fact]
        public async Task RunAsync_runs_off_the_calling_thread()
        {
            using (var t = new TestEnv())
            using (var release = new ManualResetEventSlim())
            {
                // The run waits until this thread lets it go: had RunAsync run it here, it couldn't have returned first.
                var runner = new FakeRunner { Behavior = _ => release.Wait(TimeSpan.FromSeconds(30)) ? 0 : 15 };
                Task<LaunchResult> pending = new ElevatedLauncher(t.Env, "QuietHosts.exe", runner).RunAsync(ElevatedCommand.Off());
                Assert.False(pending.IsCompleted);
                release.Set();
                LaunchResult r = await pending;
                Assert.Equal(ResultKind.Success, r.Kind);
            }
        }

        [Fact]
        public void Test_environments_start_the_child_without_runas()
        {
            using (var t = new TestEnv())
            {
                ProcessStartInfo psi = new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe").CreateStartInfo(ElevatedCommand.Allow(new[] { "a.ads-one.net" }));
                Assert.Equal(@"C:\Apps\QuietHosts.exe", psi.FileName);
                Assert.Equal("--elevated allow --count 1 a.ads-one.net --end", psi.Arguments);
                Assert.NotEqual("runas", psi.Verb);
                Assert.False(psi.UseShellExecute);
                Assert.True(psi.CreateNoWindow);
            }
        }

        [Fact]
        public void The_real_environment_uses_runas_hidden_with_shell_execute()
        {
            using (var dir = new TempDir())
            {
                // An environment that claims to be real, so only the start info is built; nothing is started.
                HostsLocation loc = HostsLocation.Unchecked(dir.Combine("etc", "hosts"), LocationKind.System);
                var env = new AppEnvironment(loc, new InMemoryMachineStore(), new InMemoryUserStore(), RealFileOps.Instance, NoDnsFlusher.Instance,
                    new FakeClock(), new FixedHostsDatabasePath(null), @"Local\QuietHosts.Test." + Guid.NewGuid().ToString("N"), TimeSpan.Zero,
                    isReal: true, requiresElevation: true);
                ProcessStartInfo psi = new ElevatedLauncher(env, @"C:\Apps\QuietHosts.exe").CreateStartInfo(ElevatedCommand.On(Level.Standard).WithSandbox(true));
                Assert.Equal("runas", psi.Verb);
                Assert.True(psi.UseShellExecute);
                Assert.Equal(ProcessWindowStyle.Hidden, psi.WindowStyle);
                Assert.Equal("--elevated on --level standard --end", psi.Arguments);
                Assert.False(psi.ErrorDialog);   // no owner window given: as before
                Assert.Equal(IntPtr.Zero, psi.ErrorDialogParentHandle);
            }
        }

        [Fact]
        public void The_window_that_started_the_change_owns_the_Windows_prompt()
        {
            using (var dir = new TempDir())
            {
                // Only start infos are built and handed to a fake runner; nothing is started.
                HostsLocation loc = HostsLocation.Unchecked(dir.Combine("etc", "hosts"), LocationKind.System);
                var env = new AppEnvironment(loc, new InMemoryMachineStore(), new InMemoryUserStore(), RealFileOps.Instance, NoDnsFlusher.Instance,
                    new FakeClock(), new FixedHostsDatabasePath(null), @"Local\QuietHosts.Test." + Guid.NewGuid().ToString("N"), TimeSpan.Zero,
                    isReal: true, requiresElevation: true);
                var owner = new IntPtr(0x12345);
                // ErrorDialog is how .NET passes the owner to ShellExecuteEx.
                ProcessStartInfo psi = new ElevatedLauncher(env, @"C:\Apps\QuietHosts.exe").CreateStartInfo(ElevatedCommand.Off(), owner);
                Assert.True(psi.ErrorDialog);
                Assert.Equal(owner, psi.ErrorDialogParentHandle);
                Assert.Equal("runas", psi.Verb);

                var runner = new FakeRunner();
                new ElevatedLauncher(env, @"C:\Apps\QuietHosts.exe", runner).Run(ElevatedCommand.Off(), null, owner);
                Assert.Equal(owner, runner.Last!.ErrorDialogParentHandle);
                Assert.True(runner.Last.ErrorDialog);
            }
            using (var t = new TestEnv())
            {
                ProcessStartInfo psi = new ElevatedLauncher(t.Env, @"C:\Apps\QuietHosts.exe").CreateStartInfo(ElevatedCommand.Off(), new IntPtr(0x12345));
                Assert.False(psi.UseShellExecute);   // no Windows prompt without runas
                Assert.False(psi.ErrorDialog);
            }
        }

#if DEBUG
        [Fact]
        public void The_sandbox_starts_the_child_with_sandbox_and_without_runas()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.SandboxAt(dir.Combine("sandbox"), null);
                ProcessStartInfo psi = new ElevatedLauncher(env, @"C:\Apps\QuietHosts.exe").CreateStartInfo(ElevatedCommand.Off());
                Assert.Equal("--sandbox --elevated off --end", psi.Arguments);
                Assert.NotEqual("runas", psi.Verb);
                Assert.False(psi.UseShellExecute);
            }
        }
#endif
    }

    public class SelfCheckTests
    {
        [Fact]
        public void Non_real_environments_read_the_hosts_file()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(HostsDocumentTests.Apply(HostsDocumentTests.B("a\r\n")));
                Assert.True(SelfCheck.Check(t.Env, "a.ads-one.net").Passed);
                Assert.False(SelfCheck.Check(t.Env, "not-blocked.example-ads.com").Passed);
                Assert.False(SelfCheck.Check(t.Env, "not a host").Passed);
            }
        }

        [Fact]
        public async Task CheckAsync_works()
        {
            using (var t = new TestEnv())
            {
                Assert.False((await SelfCheck.CheckAsync(t.Env, "a.ads-one.net")).Passed);
            }
        }

        [Fact]
        public void The_dns_cache_query_never_throws_for_unknown_names()
        {
            // DNS_QUERY_NO_WIRE_QUERY reads only the local cache: nothing goes on the network.
            SelfCheckResult r = SelfCheck.QueryDnsCache("quiethosts-selfcheck-" + Guid.NewGuid().ToString("N") + ".invalid");
            Assert.False(r.Passed);
            Assert.False(string.IsNullOrEmpty(r.Detail));
        }
    }
}
