using System;
using System.IO;
using System.Linq;
using System.Threading;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;
using static QuietHosts.Tests.HostsDocumentTests;

namespace QuietHosts.Tests
{
    /// <summary>Every ProtectionState, RefusalReason and HostsWarnings value is reachable from a crafted file or store.</summary>
    public class StatusTests
    {
        private static HostsStatus Read(TestEnv t) => StatusReader.Read(t.Env, Fixtures.SmallLists());

        [Fact]
        public void Off_for_a_plain_file()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Off, s.State);
                Assert.Equal(RefusalReason.None, s.Refusal);
                Assert.Equal(HostsWarnings.None, s.Warnings);
                Assert.False(s.CanUndo);
                Assert.True(s.FileExists);
                Assert.Equal(t.HostsPath, s.HostsPath);
                Assert.Equal(t.Location.Folder, s.HostsFolder);
                Assert.Equal(Hashing.Sha256Hex(t.ReadHosts()), s.FileSha256);
                Assert.Equal(Level.Standard, s.SelectedLevel);
            }
        }

        [Fact]
        public void Off_for_a_missing_file()
        {
            using (var t = new TestEnv())
            {
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Off, s.State);
                Assert.False(s.FileExists);
                Assert.Equal(0, s.FileSize);
            }
        }

        [Fact]
        public void On_reads_count_level_and_version_from_the_header()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Apply(Fixtures.StockHostsBytes(), Fixtures.Content(Level.Strict)));
                t.Machine.SetEnabled(true);
                t.Machine.SetLevel(Level.Standard);    // the file is the truth for what is blocked
                t.Machine.SetAllowlist(new[] { "x.five-ads.net", "gone.example-ads.com" });
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.On, s.State);
                Assert.Equal(4, s.BlockedCount);
                Assert.Equal(Level.Strict, s.BlockLevel);
                Assert.Equal(Level.Strict, s.SelectedLevel);
                Assert.Equal("2026-11-20", s.BlockListVersion);
                Assert.Equal("a.ads-one.net", s.FirstBlockedHost);
                Assert.Contains("pixel.four-ads.org", s.BlockedHosts);
                Assert.Equal(new[] { "x.five-ads.net" }, s.Allowlist);   // pruned to the lists
                Assert.Equal(1, s.AllowedCount);
                Assert.Equal(1, s.AllowedInLevelCount);
                Assert.True(s.CanUndo);
                Assert.True(s.SavedEnabled);
                Assert.Equal(Level.Standard, s.SavedLevel);
                Assert.Equal(HostsWarnings.None, s.Warnings);
            }
        }

        [Fact]
        public void Removed_when_saved_as_on_but_the_block_is_gone()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHosts());
                t.Machine.SetEnabled(true);
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Removed, s.State);
                Assert.True(s.CanUndo);
            }
        }

        [Fact]
        public void OlderList_for_only_the_legacy_block()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.LegacyLayout());
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.OlderList, s.State);
                Assert.Equal(3, s.LegacyCount);
                Assert.True(s.HasLegacyBlock);
                Assert.True(s.CanUndo);
                Assert.Equal(HostsWarnings.None, s.Warnings);
            }
        }

        [Fact]
        public void CantRead_when_the_file_stays_locked()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 10; i++) t.Files.ReadFaults.Enqueue(Faults.Sharing());
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.CantRead, s.State);
                Assert.NotNull(s.ReadError);
                Assert.Null(s.FileSha256);
                Assert.True(t.Clock.Sleeps.Count > 0 && t.Clock.Sleeps.Sum(x => x.TotalMilliseconds) < 1000);
            }
        }

        [Fact]
        public void A_briefly_locked_file_is_read_after_a_retry()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.ReadFaults.Enqueue(Faults.Lock());
                Assert.Equal(ProtectionState.Off, Read(t).State);
            }
        }

        [Fact]
        public void CantRead_when_access_is_denied()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 5; i++) t.Files.ReadFaults.Enqueue(Faults.Denied());
                Assert.Equal(ProtectionState.CantRead, Read(t).State);
            }
        }

        [Fact]
        public void Refused_HostsPathMoved()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.DatabasePath.Value = @"C:\ProgramData\Evil\etc";
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Refused, s.State);
                Assert.Equal(RefusalReason.HostsPathMoved, s.Refusal);
                Assert.Equal(@"C:\ProgramData\Evil\etc", s.MovedFolder);

                t.DatabasePath.Value = HostsDatabase.DefaultFolder.ToUpperInvariant() + @"\\";
                Assert.Equal(ProtectionState.Off, Read(t).State);

                t.DatabasePath.Throw = new System.Security.SecurityException("denied");
                Assert.Equal(RefusalReason.HostsPathMoved, Read(t).Refusal);
            }
        }

        [Fact]
        public void Refused_UnsupportedEncoding()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(new byte[] { 0xFF, 0xFE, 0x23, 0x00, 0x0D, 0x00, 0x0A, 0x00 });
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Refused, s.State);
                Assert.Equal(RefusalReason.UnsupportedEncoding, s.Refusal);
                Assert.NotNull(s.FileSha256);
            }
        }

        [Fact]
        public void Refused_TooLarge()
        {
            using (var t = new TestEnv())
            {
                using (var f = new FileStream(t.HostsPath, FileMode.Create))
                {
                    f.SetLength(Limits.MaxHostsFileBytes + 10);   // zeros, but the size is checked first
                }
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Refused, s.State);
                Assert.Equal(RefusalReason.TooLarge, s.Refusal);
            }
        }

        [Fact]
        public void Warning_EditedBlock()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(S(Apply(B("a\r\n"))).Replace("0.0.0.0 b.ads-two.com\r\n", ""));
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.On, s.State);
                Assert.True(s.Has(HostsWarnings.EditedBlock));
                Assert.Equal(4, s.BlockedCount);    // from the header
            }
        }

        [Fact]
        public void Warning_MalformedMarkers()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n# >>> QuietHosts >>>\r\n0.0.0.0 a.ads-one.net\r\n");
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.Off, s.State);
                Assert.Equal(HostsWarnings.MalformedMarkers, s.Warnings);
                Assert.True(s.CanUndo);
            }
        }

        [Fact]
        public void Warning_LargeFile()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("# " + new string('x', (int)Limits.LargeFileBytes) + "\r\n");
                HostsStatus s = Read(t);
                Assert.Equal(HostsWarnings.LargeFile, s.Warnings);
                Assert.Equal(Limits.LargeFileBytes + 4, s.BytesOutsideBlocks);
            }
        }

        [Fact]
        public void Warning_MicrosoftLinesOutside()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("0.0.0.0 vortex.data.microsoft.com\r\n");
                Assert.Equal(HostsWarnings.MicrosoftLinesOutside, Read(t).Warnings);
            }
        }

        [Fact]
        public void Warning_ChangeNotFinished_only_when_no_run_is_in_progress()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Machine.SetPending(Hashing.Sha256Hex("x"), Hashing.Sha256Hex("y"));
                HostsStatus s = Read(t);
                Assert.True(s.Has(HostsWarnings.ChangeNotFinished));
                Assert.True(s.HasPendingMarker);
                Assert.False(s.RunInProgress);

                using (var held = new ManualResetEventSlim())
                using (var release = new ManualResetEventSlim())
                {
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
                    HostsStatus during = Read(t);
                    Assert.False(during.Has(HostsWarnings.ChangeNotFinished));
                    Assert.True(during.RunInProgress);
                    release.Set();
                    thread.Join();
                }
            }
        }

        [Fact]
        public void Warning_LegacyBlockToo_with_both_blocks_and_in_the_Removed_state()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(S(Apply(B("a\r\n"))) + "# >>> block-ads.ps1 >>>\r\n0.0.0.0 c.old-ads.net\r\n# <<< block-ads.ps1 <<<\r\n");
                HostsStatus s = Read(t);
                Assert.Equal(ProtectionState.On, s.State);
                Assert.Equal(HostsWarnings.LegacyBlockToo, s.Warnings);
                Assert.Equal(1, s.LegacyCount);

                t.WriteHosts(Fixtures.LegacyLayout());
                t.Machine.SetEnabled(true);
                HostsStatus removed = Read(t);
                Assert.Equal(ProtectionState.Removed, removed.State);
                Assert.Equal(HostsWarnings.LegacyBlockToo, removed.Warnings);
            }
        }

        [Fact]
        public void Several_warnings_can_show_at_once()
        {
            using (var t = new TestEnv())
            {
                string ours = S(Apply(B("0.0.0.0 login.live.com\r\n")));
                t.WriteHosts(ours.Replace("0.0.0.0 a.ads-one.net\r\n", "") + "# <<< QuietHosts <<<\r\n");
                t.Machine.SetPending(Hashing.Sha256Hex("x"), Hashing.Sha256Hex("y"));
                HostsStatus s = Read(t);
                Assert.Equal(HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished, s.Warnings);
            }
        }

        [Fact]
        public void Pending_level_and_first_run_come_from_the_user_store()
        {
            using (var t = new TestEnv())
            {
                t.User.SetPendingLevel(Level.Strict);
                t.User.SetFirstRunDone(true);
                HostsStatus s = Read(t);
                Assert.Equal(Level.Strict, s.PendingLevel);
                Assert.Equal(Level.Strict, s.SelectedLevel);
                Assert.True(s.FirstRunDone);
            }
        }

        [Fact]
        public void Reading_the_status_writes_nothing()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.LegacyLayout());
                byte[] before = t.ReadHosts();
                int writes = t.Machine.WriteCount;
                Read(t);
                Assert.Equal(before, t.ReadHosts());
                Assert.Equal(writes, t.Machine.WriteCount);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Empty(t.BackupNames());
            }
        }

        [Fact]
        public void The_status_carries_the_notice_fingerprints_and_the_Microsoft_lines()
        {
            using (var t = new TestEnv())
            {
                string ours = S(Apply(B("0.0.0.0 login.live.com\r\n# 0.0.0.0 www.bing.com\r\n10.0.0.5 nas.home.lan\r\n0.0.0.0 vortex.data.microsoft.com \r\n")));
                t.WriteHosts(ours);
                HostsStatus s = Read(t);
                HostsDocument doc = HostsDocument.Parse(t.ReadHosts());
                Assert.Equal(ProtectionState.On, s.State);
                Assert.True(s.Has(HostsWarnings.MicrosoftLinesOutside));
                Assert.Equal(2, s.MicrosoftLineCount);
                Assert.Equal(new[] { "0.0.0.0 login.live.com", "0.0.0.0 vortex.data.microsoft.com" }, s.MicrosoftLines);
                Assert.Equal(doc.MicrosoftLinesSha256, s.MicrosoftLinesSha256);
                Assert.NotNull(s.MicrosoftLinesSha256);
                Assert.Equal(doc.BlockLinesSha256, s.BlockLinesSha256);
                Assert.NotNull(s.BlockLinesSha256);

                // Off: no block, but lines outside it still count.
                t.WriteHosts("0.0.0.0 login.live.com\r\n");
                HostsStatus off = Read(t);
                Assert.Equal(ProtectionState.Off, off.State);
                Assert.Null(off.BlockLinesSha256);
                Assert.Equal(1, off.MicrosoftLineCount);

                // No such lines and no block: nulls and empties.
                t.WriteHosts(Fixtures.StockHosts());
                HostsStatus plain = Read(t);
                Assert.Null(plain.BlockLinesSha256);
                Assert.Null(plain.MicrosoftLinesSha256);
                Assert.Empty(plain.MicrosoftLines);
                Assert.Equal(0, plain.MicrosoftLineCount);
            }
        }

        [Fact]
        public void Refused_and_cant_read_statuses_have_no_fingerprints_or_lines()
        {
            using (var t = new TestEnv())
            {
                // Microsoft lines and our block, then a NUL byte: the file is refused as a whole.
                string ours = S(Apply(B("0.0.0.0 login.live.com\r\n")));
                t.WriteHosts(B(ours + "\0\r\n"));
                HostsStatus refused = Read(t);
                Assert.Equal(ProtectionState.Refused, refused.State);
                AssertNoFingerprints(refused);

                t.WriteHosts(ours);
                for (int i = 0; i < 10; i++) t.Files.ReadFaults.Enqueue(Faults.Sharing());
                HostsStatus cantRead = Read(t);
                Assert.Equal(ProtectionState.CantRead, cantRead.State);
                AssertNoFingerprints(cantRead);

                t.DatabasePath.Value = @"C:\ProgramData\Elsewhere\etc";
                HostsStatus moved = Read(t);
                Assert.Equal(RefusalReason.HostsPathMoved, moved.Refusal);
                AssertNoFingerprints(moved);
            }
        }

        private static void AssertNoFingerprints(HostsStatus s)
        {
            Assert.Null(s.BlockLinesSha256);
            Assert.Null(s.MicrosoftLinesSha256);
            Assert.Empty(s.MicrosoftLines);
            Assert.Equal(0, s.MicrosoftLineCount);
        }

        [Fact]
        public void Every_enum_value_was_reached()
        {
            // A guard against new values without a status test: the tests above cover these.
            Assert.Equal(6, Enum.GetValues(typeof(ProtectionState)).Length);
            Assert.Equal(4, Enum.GetValues(typeof(RefusalReason)).Length);
            Assert.Equal(7, Enum.GetValues(typeof(HostsWarnings)).Length);
        }
    }
}
