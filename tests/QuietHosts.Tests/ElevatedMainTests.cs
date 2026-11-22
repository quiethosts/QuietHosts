using System;
using System.IO;
using System.Linq;
using System.Threading;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    /// <summary>The elevated verbs against a test environment (no admin rights needed: it doesn't require elevation).</summary>
    public class ElevatedMainTests
    {
        private static readonly BlockLists Lists = BlockLists.LoadEmbedded();
        private static string DefaultHost => Lists.DefaultEntries[0].Host;
        private static string StrictHost => Lists.StrictEntries[0].Host;

        /// <summary>
        /// The elevated grammar from the words a test writes: adds "--count N" after allow/block and the final "--end",
        /// as ElevatedCommand.ToArgs does (ElevatedCommandTests pins the literal grammar).
        /// </summary>
        private static string[] Cmd(params string[] words)
        {
            var args = new System.Collections.Generic.List<string>(words);
            if (args.Count >= 2 && args[0] == "--elevated" && (args[1] == "allow" || args[1] == "block"))
                args.InsertRange(2, new[] { "--count", (args.Count - 2).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            args.Add("--end");
            return args.ToArray();
        }

        private static int Run(TestEnv t, params string[] words) => ElevatedMain.Run(Cmd(words), t.Env, () => false);

        [Fact]
        public void On_standard_writes_the_default_tier_and_saves_the_state()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                HostsDocument doc = t.Document();
                Assert.True(doc.HasBlock);
                Assert.False(doc.BlockEdited);
                Assert.Equal(Level.Standard, doc.Header!.Level);
                Assert.Equal(Lists.Version, doc.Header.ListVersion);
                Assert.Equal(Lists.StandardCount, doc.BlockHosts.Count);
                Assert.DoesNotContain(StrictHost, doc.BlockHosts);
                MachineState m = t.Machine.Read();
                Assert.True(m.Enabled);
                Assert.Equal(Level.Standard, m.Level);
                Assert.False(m.HasPending);
                Assert.Contains("command: on --level standard", m.LastRun);
                Assert.Contains("result: Success (exit 0)", m.LastRun);
                Assert.Equal(1, t.Flusher.Calls);
                Assert.Equal(ProtectionState.On, StatusReader.Read(t.Env, Lists).State);
            }
        }

        [Fact]
        public void On_strict_adds_the_strict_tier()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "strict"));
                HostsDocument doc = t.Document();
                Assert.Equal(Level.Strict, doc.Header!.Level);
                Assert.Equal(Lists.StrictTotalCount, doc.BlockHosts.Count);
                Assert.Contains(StrictHost, doc.BlockHosts);
                Assert.Equal(Level.Strict, t.Machine.Read().Level);
            }
        }

        [Fact]
        public void Off_removes_the_block_and_keeps_level_and_allowlist()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "strict"));
                Assert.Equal(0, Run(t, "--elevated", "allow", DefaultHost));
                Assert.Equal(0, Run(t, "--elevated", "off"));
                Assert.Equal(Fixtures.StockHosts(), t.ReadHostsText());
                MachineState m = t.Machine.Read();
                Assert.False(m.Enabled);
                Assert.Equal(Level.Strict, m.Level);
                Assert.Equal(new[] { DefaultHost }, m.Allowlist);
            }
        }

        [Fact]
        public void Allow_and_block_again_while_on()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(0, Run(t, "--elevated", "allow", DefaultHost.ToUpperInvariant()));
                Assert.DoesNotContain(DefaultHost, t.Document().BlockHosts);
                Assert.Equal(new[] { DefaultHost }, t.Machine.Read().Allowlist);
                Assert.Equal(Lists.StandardCount - 1, t.Document().Header!.HostCount);
                Assert.Contains("command: allow (1 addresses)", t.Machine.Read().LastRun);
                Assert.DoesNotContain(DefaultHost, t.Machine.Read().LastRun);   // LastRun has the count, not the names

                Assert.Equal(0, Run(t, "--elevated", "block", DefaultHost));
                Assert.Contains(DefaultHost, t.Document().BlockHosts);
                Assert.Empty(t.Machine.Read().Allowlist);
                Assert.True(t.Machine.Read().Enabled);
            }
        }

        [Fact]
        public void Allow_while_off_changes_only_the_allowlist()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "allow", DefaultHost, StrictHost));
                Assert.Equal(Fixtures.StockHosts(), t.ReadHostsText());
                Assert.Equal(new[] { DefaultHost, StrictHost }.OrderBy(h => h, StringComparer.Ordinal), t.Machine.Read().Allowlist);
                Assert.False(t.Machine.Read().Enabled);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Empty(t.BackupNames());

                // The next Turn on leaves them out.
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "strict"));
                Assert.DoesNotContain(DefaultHost, t.Document().BlockHosts);
                Assert.DoesNotContain(StrictHost, t.Document().BlockHosts);
            }
        }

        [Fact]
        public void Allow_rewrites_the_block_when_it_is_in_the_file_though_Enabled_wasnt_saved()
        {
            using (var t = new TestEnv())
            {
                // Turn on writes and verifies the file, then saving Enabled=1 fails: the window shows On.
                t.Machine.OnWrite = name => { if (name == "Enabled") throw new InvalidOperationException("registry"); };
                Assert.Equal(15, Run(t, "--elevated", "on", "--level", "standard"));
                t.Machine.OnWrite = null;
                Assert.True(t.Document().HasBlock);
                Assert.False(t.Machine.Read().Enabled);
                Assert.Equal(ProtectionState.On, StatusReader.Read(t.Env, Lists).State);

                Assert.Equal(0, Run(t, "--elevated", "allow", DefaultHost));
                Assert.DoesNotContain(DefaultHost, t.Document().BlockHosts);
                Assert.Equal(Lists.StandardCount - 1, t.Document().Header!.HostCount);
                MachineState m = t.Machine.Read();
                Assert.Equal(new[] { DefaultHost }, m.Allowlist);
                Assert.True(m.Enabled);
                Assert.Equal(Level.Standard, m.Level);
            }
        }

        [Fact]
        public void Allow_after_a_restored_backup_follows_the_block_in_the_file()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "strict"));
                byte[] strict = t.ReadHosts();
                Assert.Equal(0, Run(t, "--elevated", "undo"));
                t.WriteHosts(strict);    // the user restores a backup: the Strict block is back, nothing is saved
                Assert.False(t.Machine.Read().Exists);

                Assert.Equal(0, Run(t, "--elevated", "allow", StrictHost));
                HostsDocument doc = t.Document();
                Assert.Equal(Level.Strict, doc.Header!.Level);   // not the saved (missing) level
                Assert.DoesNotContain(StrictHost, doc.BlockHosts);
                Assert.Equal(Lists.StrictTotalCount - 1, doc.Header.HostCount);
                Assert.True(t.Machine.Read().Enabled);
                Assert.Equal(Level.Strict, t.Machine.Read().Level);

                // A Strict block with Level=standard saved: the block's level wins, and the saved level follows it.
                t.Machine.SetLevel(Level.Standard);
                Assert.Equal(0, Run(t, "--elevated", "block", StrictHost));
                Assert.Equal(Level.Strict, t.Document().Header!.Level);
                Assert.Contains(StrictHost, t.Document().BlockHosts);
                Assert.Equal(Level.Strict, t.Machine.Read().Level);
            }
        }

        [Fact]
        public void Off_after_a_deleted_begin_marker_leaves_no_blocking_lines()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                t.WriteHosts(t.ReadHostsText().Replace("# >>> QuietHosts >>>\r\n", ""));
                HostsStatus s = StatusReader.Read(t.Env, Lists);
                Assert.Equal(ProtectionState.Removed, s.State);
                Assert.True(s.Has(HostsWarnings.MalformedMarkers));

                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(Lists.StandardCount, t.Document().Header!.HostCount);
                Assert.Equal(Lists.StandardCount, CountZeroLines(t.ReadHostsText()));
                Assert.Equal(0, Run(t, "--elevated", "off"));
                Assert.Equal(0, CountZeroLines(t.ReadHostsText()));
                Assert.DoesNotContain("QuietHosts", t.ReadHostsText());
            }
        }

        [Theory]
        [InlineData("off")]
        [InlineData("undo")]
        public void Off_after_line_ends_were_translated_twice_leaves_no_blocking_lines(string verb)
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                t.WriteHosts(t.ReadHostsText().Replace("\r\n", "\r\r\n"));
                Assert.True(StatusReader.Read(t.Env, Lists).Has(HostsWarnings.EditedBlock));
                Assert.Equal(0, Run(t, "--elevated", verb));
                Assert.Equal(0, CountZeroLines(t.ReadHostsText()));
            }
        }

        private static int CountZeroLines(string text) =>
            text.Split('\n').Count(l => l.StartsWith("0.0.0.0", StringComparison.Ordinal));

        [Theory]
        [InlineData("# >>> QuietHosts >>>\r\n")]
        [InlineData("# <<< QuietHosts <<<\r\n")]
        public void Off_after_a_deleted_marker_gives_the_file_back_exactly(string marker)
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                byte[] on = t.ReadHosts();
                t.WriteHosts(t.ReadHostsText().Replace(marker, ""));
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(on, t.ReadHosts());   // the repair added no blank line
                t.WriteHosts(t.ReadHostsText().Replace(marker, ""));
                Assert.Equal(0, Run(t, "--elevated", "off"));
                Assert.Equal(Fixtures.StockHosts(), t.ReadHostsText());
            }
        }

        [Fact]
        public void A_hand_disabled_entry_is_allowed_even_after_line_ends_were_translated_twice()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                t.WriteHosts(t.ReadHostsText().Replace("0.0.0.0 " + DefaultHost + "\r\n", "#0.0.0.0 " + DefaultHost + "\r\n").Replace("\r\n", "\r\r\n"));
                HostsStatus s = StatusReader.Read(t.Env, Lists);
                Assert.Equal(ProtectionState.On, s.State);
                Assert.Equal(Lists.StandardCount - 1, s.BlockedHosts.Count);   // the rewritten entries still block
                Assert.DoesNotContain(DefaultHost, s.BlockedHosts);

                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(new[] { DefaultHost }, t.Machine.Read().Allowlist);
                Assert.DoesNotContain("#0.0.0.0 " + DefaultHost, t.ReadHostsText());   // not kept as a foreign line below the block
                Assert.DoesNotContain(DefaultHost, t.Document().BlockHosts);
                Assert.False(t.Document().BlockEdited);
                Assert.Equal(Lists.StandardCount - 1, t.Document().BlockHosts.Count);
            }
        }

        [Fact]
        public void RunInProgress_is_true_only_while_an_elevated_run_holds_the_write_mutex()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                Assert.False(StatusReader.Read(t.Env, Lists).RunInProgress);
                HostsStatus? during = null;
                t.Clock.OnSleep = d => { if (d == HostsWriter.RecheckDelay) during = StatusReader.Read(t.Env, Lists); };
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.NotNull(during);
                Assert.True(during!.RunInProgress);
                Assert.False(during.Has(HostsWarnings.ChangeNotFinished));   // its pending marker is no unfinished change
                Assert.True(during.HasPendingMarker);
                HostsStatus after = StatusReader.Read(t.Env, Lists);
                Assert.False(after.RunInProgress);
                Assert.False(after.HasPendingMarker);
            }
        }

        [Fact]
        public void Allow_and_block_take_only_listed_hosts()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(15, Run(t, "--elevated", "allow", "not-in-the-lists.example-ads.com"));
                Assert.Equal(15, Run(t, "--elevated", "block", "not-in-the-lists.example-ads.com"));
                Assert.Empty(t.Machine.Read().Allowlist);
                Assert.Equal(0, t.Files.HostsWrites);
            }
        }

        [Fact]
        public void Undo_removes_both_blocks_and_the_saved_state_but_keeps_backups()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.LegacyLayout());
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.False(t.Document().HasLegacyBlock);
                // Bring an old block back to prove undo removes both.
                File.AppendAllText(t.HostsPath, "\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 c.old-ads.net\r\n# <<< block-ads.ps1 <<<\r\n");
                Assert.Equal(0, Run(t, "--elevated", "undo"));
                Assert.Equal(Fixtures.StockHosts(), t.ReadHostsText());
                Assert.False(t.Machine.Read().Exists);
                Assert.NotEmpty(t.BackupNames());
                Assert.Equal(Fixtures.LegacyLayout(), File.ReadAllText(Path.Combine(t.Location.Folder, Backups.OriginalName)));
            }
        }

        [Fact]
        public void Busy_when_another_run_holds_the_mutex()
        {
            using (var t = new TestEnv())
            using (var held = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                t.WriteHosts("a\r\n");
                var thread = new Thread(() =>
                {
                    using (var m = new Mutex(true, t.Env.WriteMutexName))
                    {
                        held.Set();
                        release.Wait();
                        m.ReleaseMutex();
                    }
                });
                thread.Start();
                held.Wait();
                try
                {
                    Assert.Equal(13, Run(t, "--elevated", "on", "--level", "standard"));
                }
                finally
                {
                    release.Set();
                    thread.Join();
                }
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Machine.WriteCount);
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
            }
        }

        [Fact]
        public void Refused_files_exit_16()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(new byte[] { 0xFE, 0xFF, 0x00, 0x23 });
                Assert.Equal(16, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(16, Run(t, "--elevated", "off"));
                Assert.Equal(16, Run(t, "--elevated", "undo"));
                Assert.True(t.Machine.Read().Exists);   // undo keeps the state when it couldn't change the file
                Assert.Contains("refusal: UnsupportedEncoding", t.Machine.Read().LastRun);
                Assert.Empty(t.BackupNames());
            }
            using (var t = new TestEnv())
            {
                t.DatabasePath.Value = @"C:\Elsewhere\etc";
                Assert.Equal(16, Run(t, "--elevated", "on", "--level", "standard"));
            }
        }

        [Fact]
        public void Not_elevated_outside_the_sandbox_and_test_environment_exits_15()
        {
            using (var t = new TestEnv(requiresElevation: true))
            {
                t.WriteHosts("a\r\n");
                Assert.Equal(15, ElevatedMain.Run(Cmd("--elevated", "on", "--level", "standard"), t.Env, () => false));
                Assert.Equal(15, ElevatedMain.Run(new[] { "--elevated", "off" }, t.Env, () => false));   // a rejected line, not elevated
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Machine.WriteCount);
                Assert.Equal(15, ElevatedMain.Run(Cmd("--elevated", "off"), t.Env, () => throw new InvalidOperationException()));
                Assert.Equal(0, ElevatedMain.Run(Cmd("--elevated", "on", "--level", "standard"), t.Env, () => true));
            }
        }

        [Fact]
        public void Bad_arguments_exit_15_and_change_nothing_but_LastRun()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                Assert.Equal(15, Run(t, "--elevated", "on", "--level", "medium"));
                Assert.Equal(15, Run(t, "--elevated", "allow", @"C:\Windows\System32\drivers\etc\hosts"));
                MachineState m = t.Machine.Read();
                Assert.Contains("command: rejected: paths are not accepted (6 arguments, ", m.LastRun);
                Assert.Contains("result: Unexpected (exit 15)", m.LastRun);
                Assert.DoesNotContain(@"drivers\etc", m.LastRun);   // never the arguments themselves
                int writes = t.Machine.WriteCount;

                // Lines that aren't meant for an elevated run aren't even recorded.
                Assert.Equal(15, Run(t));
                Assert.Equal(15, ElevatedMain.Run(new string[0], t.Env, () => false));
                Assert.Equal(15, ElevatedMain.Run(new[] { "x", "--elevated", "off", "--end" }, t.Env, () => false));
                Assert.Equal(writes, t.Machine.WriteCount);

                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Files.HostsReads);
                Assert.Empty(t.BackupNames());
                m = t.Machine.Read();
                Assert.False(m.Enabled);
                Assert.False(m.HasLevel);
                Assert.Empty(m.Allowlist);
                Assert.False(m.HasPending);
            }
        }

        [Fact]
        public void A_command_line_cut_short_exits_15_and_says_so_in_LastRun()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                byte[] on = t.ReadHosts();
                var hosts = Lists.DefaultEntries.Take(100).Select(e => e.Host).ToArray();
                string[] full = ElevatedCommand.Allow(hosts).ToArgs();
                // Cut at an address boundary: what a too-short command line buffer would deliver.
                string[] cut = full.Take(full.Length - 8).ToArray();
                Assert.Equal(15, ElevatedMain.Run(cut, t.Env, () => false));
                Assert.Equal(on, t.ReadHosts());
                Assert.Empty(t.Machine.Read().Allowlist);
                Assert.Contains("command: rejected: missing --end (" + cut.Length + " arguments, ", t.Machine.Read().LastRun);
                foreach (string h in hosts) Assert.DoesNotContain(h, t.Machine.Read().LastRun);
            }
        }

        [Fact]
        public void A_sandbox_command_needs_the_sandbox_environment()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(15, ElevatedMain.Run(ElevatedCommand.Off().WithSandbox(true), t.Env, () => false));
            }
        }

        [Fact]
        public void Hand_disabled_lines_move_into_the_allowlist()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                string text = t.ReadHostsText().Replace("0.0.0.0 " + DefaultHost + "\r\n", "#0.0.0.0 " + DefaultHost + "\r\n");
                t.WriteHosts(text);
                Assert.True(t.Document().BlockEdited);
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(new[] { DefaultHost }, t.Machine.Read().Allowlist);
                Assert.DoesNotContain("#0.0.0.0 " + DefaultHost, t.ReadHostsText());
                Assert.False(t.Document().BlockEdited);
            }
        }

        [Fact]
        public void Names_mapped_outside_are_never_duplicated()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("192.168.1.20 " + DefaultHost + "\r\n");
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.DoesNotContain(DefaultHost, t.Document().BlockHosts);
                Assert.StartsWith("192.168.1.20 " + DefaultHost + "\r\n", t.ReadHostsText());
            }
        }

        [Fact]
        public void Stale_allowlist_entries_are_pruned_at_each_write()
        {
            using (var t = new TestEnv())
            {
                t.Machine.SetAllowlist(new[] { "gone.example-ads.com", DefaultHost });
                Assert.Equal(0, Run(t, "--elevated", "on", "--level", "standard"));
                Assert.Equal(new[] { DefaultHost }, t.Machine.Read().Allowlist);
            }
        }

        [Fact]
        public void Every_run_recovers_a_pending_marker_first()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Machine.SetPending(Hashing.Sha256Hex("x"), Hashing.Sha256Hex("y"));
                Assert.Equal(0, Run(t, "--elevated", "allow", DefaultHost));   // no hosts write needed while off
                MachineState m = t.Machine.Read();
                Assert.False(m.HasPending);
                Assert.Contains("log: recovery: the hosts file matched neither", m.LastRun);
            }
        }

        [Fact]
        public void Exceptions_exit_15_with_details_in_LastRun()
        {
            using (var t = new TestEnv())
            {
                t.Machine.OnWrite = name => { if (name == "Enabled") throw new InvalidOperationException("boom"); };
                Assert.Equal(15, Run(t, "--elevated", "on", "--level", "standard"));
                string lastRun = t.Machine.Read().LastRun!;
                Assert.Contains("result: Unexpected (exit 15)", lastRun);
                Assert.Contains("error: System.InvalidOperationException: boom", lastRun);
                Assert.True(lastRun.Length <= Limits.MaxLastRunChars);
            }
        }

        [Fact]
        public void A_write_failure_keeps_the_saved_state()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.SkipWrites = true;
                Assert.Equal(10, Run(t, "--elevated", "on", "--level", "strict"));
                MachineState m = t.Machine.Read();
                Assert.False(m.Enabled);
                Assert.False(m.HasLevel);
                Assert.Contains("result: AccessDenied (exit 10)", m.LastRun);
            }
        }
    }
}
