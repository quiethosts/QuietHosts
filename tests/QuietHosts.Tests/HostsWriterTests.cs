using System;
using System.IO;
using System.Linq;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;
using static QuietHosts.Tests.HostsDocumentTests;

namespace QuietHosts.Tests
{
    public class HostsWriterTests
    {
        private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

        private static WriteOutcome ApplyBlock(TestEnv t, HostsWriter? w = null) =>
            (w ?? new HostsWriter(t.Env)).Write(doc => doc.Apply(Fixtures.Content(), Guard.BuiltIn));

        private static WriteOutcome RemoveBlock(TestEnv t) => new HostsWriter(t.Env).Write(doc => doc.Remove());

        private static byte[] Corrupt(byte[] b)
        {
            var c = (byte[])b.Clone();
            c[c.Length / 2] ^= 0x20;
            return c;
        }

        [Fact]
        public void Success_writes_after_a_verified_backup()
        {
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(ResultKind.Success, o.Kind);
                Assert.True(o.Written);
                Assert.Equal(Apply(stock), t.ReadHosts());
                string[] backups = t.BackupNames();
                Assert.Equal(2, backups.Length);
                Assert.Contains(Backups.OriginalName, backups);
                string stamped = backups.Single(Backups.IsTimestampedName);
                Assert.Equal(stock, File.ReadAllBytes(Path.Combine(t.Location.Folder, stamped)));
                Assert.Equal(stock, File.ReadAllBytes(Path.Combine(t.Location.Folder, Backups.OriginalName)));
                Assert.Equal(Path.Combine(t.Location.Folder, stamped), o.BackupPath);
                Assert.Equal(1, t.Flusher.Calls);
                Assert.Contains(TwoSeconds, t.Clock.Sleeps);
                Assert.False(t.Machine.Read().HasPending);
                Assert.Equal(Hashing.Sha256Hex(stock), o.BeforeSha256);
                Assert.Equal(Hashing.Sha256Hex(t.ReadHosts()), o.AfterSha256);
                Assert.Contains(w.Log, l => l.StartsWith("backup: ", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void A_missing_hosts_file_is_created()
        {
            using (var t = new TestEnv())
            {
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(Apply(null), t.ReadHosts());
                Assert.Empty(File.ReadAllBytes(Path.Combine(t.Location.Folder, Backups.OriginalName)));
            }
        }

        [Fact]
        public void The_original_backup_is_created_once_and_never_overwritten()
        {
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(ResultKind.Success, RemoveBlock(t).Kind);
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(stock, File.ReadAllBytes(Path.Combine(t.Location.Folder, Backups.OriginalName)));
                Assert.Equal(3, t.BackupNames().Count(Backups.IsTimestampedName));
            }
        }

        [Fact]
        public void Backups_are_pruned_to_the_ten_newest()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHostsBytes());
                for (int i = 0; i < 12; i++)
                    Assert.Equal(ResultKind.Success, (i % 2 == 0 ? ApplyBlock(t) : RemoveBlock(t)).Kind);
                // An old block-ads.ps1 backup is left alone.
                File.WriteAllText(Path.Combine(t.Location.Folder, "hosts.bak-20260101-000000-000"), "old");
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                var stamped = t.BackupNames().Where(Backups.IsTimestampedName).ToList();
                Assert.Equal(10, stamped.Count);
                Assert.Contains(Backups.OriginalName, t.BackupNames());
                Assert.True(File.Exists(Path.Combine(t.Location.Folder, "hosts.bak-20260101-000000-000")));
            }
        }

        [Fact]
        public void A_backup_identical_to_the_newest_is_not_written_again()
        {
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                WriteOutcome first = ApplyBlock(t);
                t.WriteHosts(stock);                 // someone restored the old file
                WriteOutcome second = ApplyBlock(t);
                Assert.Equal(ResultKind.Success, second.Kind);
                Assert.Single(t.BackupNames(), Backups.IsTimestampedName);
                Assert.Equal(first.BackupPath, second.BackupPath);
            }
        }

        [Fact]
        public void The_read_only_attribute_is_cleared_and_restored()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHostsBytes());
                File.SetAttributes(t.HostsPath, FileAttributes.ReadOnly | FileAttributes.Archive);
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.True((File.GetAttributes(t.HostsPath) & FileAttributes.ReadOnly) != 0);
                Assert.Equal(Apply(Fixtures.StockHostsBytes()), t.ReadHosts());
            }
        }

        [Fact]
        public void Sharing_violations_are_retried()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.WriteFaults.Enqueue(Faults.Sharing());
                t.Files.WriteFaults.Enqueue(Faults.Lock());
                t.Files.WriteFaults.Enqueue(Faults.Sharing());
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(new[] { 50.0, 100.0, 200.0, 2000.0 }, t.Clock.Sleeps.Select(s => s.TotalMilliseconds).ToArray());
            }
        }

        [Fact]
        public void Lasting_sharing_violations_return_FileLocked()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 7; i++) t.Files.WriteFaults.Enqueue(Faults.Sharing());
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.FileLocked, o.Kind);
                Assert.False(o.Written);
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(new[] { 50.0, 100.0, 200.0, 400.0, 800.0, 1600.0 }, t.Clock.Sleeps.Select(s => s.TotalMilliseconds).ToArray());
                Assert.False(t.Machine.Read().HasPending);
                Assert.Equal(0, t.Flusher.Calls);
            }
        }

        [Fact]
        public void Access_denied_is_retried_twice_then_AccessDenied()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 3; i++) t.Files.WriteFaults.Enqueue(Faults.Denied());
                Assert.Equal(ResultKind.AccessDenied, ApplyBlock(t).Kind);
                Assert.Equal(new[] { 250.0, 250.0 }, t.Clock.Sleeps.Select(s => s.TotalMilliseconds).ToArray());
                Assert.Equal("a\r\n", t.ReadHostsText());
            }
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 2; i++) t.Files.WriteFaults.Enqueue(Faults.Denied());
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
            }
        }

        [Fact]
        public void A_silently_skipped_write_is_AccessDenied()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.SkipWrites = true;
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.AccessDenied, o.Kind);
                Assert.Equal(1, t.Files.HostsWrites);    // no rollback attempt
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Flusher.Calls);
            }
        }

        [Fact]
        public void A_corrupted_read_back_is_rolled_back_and_Reverted()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.TransformWrite = (n, b) => n == 1 ? Corrupt(b) : b;
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Reverted, o.Kind);
                Assert.Equal(2, t.Files.HostsWrites);
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.False(t.Machine.Read().HasPending);
            }
        }

        [Fact]
        public void A_revert_within_two_seconds_is_Reverted_and_not_fought()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Clock.OnSleep = d => { if (d == TwoSeconds) File.WriteAllText(t.HostsPath, "a\r\n"); };
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Reverted, o.Kind);
                Assert.Equal(1, t.Files.HostsWrites);
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Flusher.Calls);
                Assert.False(t.Machine.Read().HasPending);   // back to the old version: nothing unfinished
            }
        }

        [Fact]
        public void A_failed_rollback_is_Unexpected_and_logs_the_backup()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.TransformWrite = (n, b) => Corrupt(b);
                t.Files.FailWritesAfterFirst = Faults.DiskFull();
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(ResultKind.Unexpected, o.Kind);
                Assert.NotNull(o.BackupPath);
                Assert.Contains(w.Log, l => l.StartsWith("rollback failed", StringComparison.Ordinal) && l.Contains(o.BackupPath!));
                // The file is neither version: the marker stays, so the window shows ChangeNotFinished.
                Assert.True(t.Machine.Read().HasPending);
                Assert.Contains(w.Log, l => l.StartsWith("pending marker kept", StringComparison.Ordinal));
                var next = new HostsWriter(t.Env);
                next.Recover();
                Assert.Contains(next.Log, l => l.Contains("matched neither"));
            }
        }

        // A long block that a shorter one replaces: a write cut before SetLength leaves the old tail behind.
        private static byte[] LongBlockFile() =>
            Apply(Fixtures.StockHostsBytes(), Fixtures.Content(Level.Strict, Enumerable.Range(0, 200).Select(i => "h" + i + ".many-ads.com").ToArray()));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_lock_or_denial_after_the_bytes_went_out_is_rolled_back(bool locked)
        {
            using (var t = new TestEnv())
            {
                byte[] before = LongBlockFile();
                t.WriteHosts(before);
                int attempts = locked ? 7 : 3;
                for (int i = 0; i < attempts; i++) t.Files.WriteThenFaults.Enqueue(locked ? (Exception)Faults.Lock() : Faults.Denied());
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(locked ? ResultKind.FileLocked : ResultKind.AccessDenied, o.Kind);
                Assert.Equal(before, t.ReadHosts());   // so the window's "nothing was changed" is true
                Assert.Equal(attempts + 1, t.Files.HostsWrites);
                Assert.Contains(w.Log, l => l.StartsWith("rolled back", StringComparison.Ordinal));
                Assert.False(t.Machine.Read().HasPending);
                Assert.Equal(0, t.Flusher.Calls);
            }
        }

        [Fact]
        public void A_torn_write_that_cant_be_rolled_back_is_Unexpected_and_keeps_the_marker()
        {
            using (var t = new TestEnv())
            {
                byte[] before = LongBlockFile();
                t.WriteHosts(before);
                t.Files.WriteThenFaults.Enqueue(Faults.Lock());   // the first attempt tears the file
                t.Files.FailWritesAfterFirst = Faults.Lock();     // every later write fails before writing anything
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(ResultKind.Unexpected, o.Kind);
                Assert.NotEqual(before, t.ReadHosts());
                Assert.True(t.Machine.Read().HasPending);
                Assert.Contains(w.Log, l => l.StartsWith("rollback failed", StringComparison.Ordinal) && l.Contains(o.BackupPath!));
            }
        }

        [Fact]
        public void A_lock_that_also_blocks_the_check_is_FileLocked_and_keeps_the_marker()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                for (int i = 0; i < 7; i++) t.Files.WriteFaults.Enqueue(Faults.Sharing());
                t.Files.BeforeHostsRead = n => { if (n == 3) for (int i = 0; i < 7; i++) t.Files.ReadFaults.Enqueue(Faults.Sharing()); };
                Assert.Equal(ResultKind.FileLocked, ApplyBlock(t).Kind);
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.True(t.Machine.Read().HasPending);
                var next = new HostsWriter(t.Env);
                next.Recover();
                Assert.False(t.Machine.Read().HasPending);
                Assert.Contains(next.Log, l => l.Contains("hadn't been written"));
            }
        }

        [Fact]
        public void A_revert_to_a_third_version_keeps_the_pending_marker()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Clock.OnSleep = d => { if (d == TwoSeconds) File.WriteAllText(t.HostsPath, "reset by another program\r\n"); };
                Assert.Equal(ResultKind.Reverted, ApplyBlock(t).Kind);
                Assert.True(t.Machine.Read().HasPending);
            }
        }

        [Fact]
        public void A_backup_with_an_older_name_than_the_kept_ones_is_never_pruned()
        {
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                File.WriteAllBytes(Path.Combine(t.Location.Folder, Backups.OriginalName), stock);
                // The clock went back: ten backups have later names than now.
                for (int i = 0; i < 10; i++)
                    File.WriteAllText(Path.Combine(t.Location.Folder, Backups.NameFor(new DateTime(2030, 1, 1, 0, i, 0, DateTimeKind.Utc))), "later " + i);
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(ResultKind.Success, o.Kind);
                Assert.True(File.Exists(o.BackupPath));
                Assert.Equal(stock, File.ReadAllBytes(o.BackupPath!));
                Assert.StartsWith("hosts.quiethosts-20261120-0930", Path.GetFileName(o.BackupPath));   // named from UTC
                Assert.Equal(10, t.BackupNames().Count(Backups.IsTimestampedName));
                string oldest = Backups.NameFor(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                Assert.False(File.Exists(Path.Combine(t.Location.Folder, oldest)));
                Assert.Contains("backup: pruned " + oldest, w.Log);
            }
        }

        [Fact]
        public void Pruning_keeps_the_named_backup_and_the_newest_others()
        {
            using (var dir = new TempDir())
            {
                for (int i = 0; i < 12; i++) File.WriteAllText(dir.Combine(Backups.NameFor(new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc))), "x");
                string keep = Backups.NameFor(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var log = new System.Collections.Generic.List<string>();
                Assert.Equal(2, Backups.Prune(RealFileOps.Instance, dir.Path, 10, log, keep));
                var left = Backups.ListTimestamped(RealFileOps.Instance, dir.Path);
                Assert.Equal(10, left.Count);
                Assert.Contains(keep, left);
                Assert.Equal(Backups.NameFor(new DateTime(2026, 1, 1, 0, 0, 11, DateTimeKind.Utc)), left[0]);
                Assert.Equal(2, log.Count(l => l.StartsWith("backup: pruned ", StringComparison.Ordinal)));
            }
        }

        [Fact]
        public void A_partial_original_backup_is_removed_and_written_again_later()
        {
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                t.Files.CreatePartialFault = n => n == 2 ? Faults.DiskFull() : null;   // 1: timestamped, 2: original
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.Equal(stock, t.ReadHosts());
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.False(File.Exists(Path.Combine(t.Location.Folder, Backups.OriginalName)));

                t.Files.CreatePartialFault = null;
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(stock, File.ReadAllBytes(Path.Combine(t.Location.Folder, Backups.OriginalName)));
            }
        }

        [Fact]
        public void A_partial_timestamped_backup_is_removed()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHostsBytes());
                t.Files.CreatePartialFault = n => Faults.DiskFull();
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.Empty(t.BackupNames());
                Assert.Equal(0, t.Files.HostsWrites);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_partial_original_backup_left_before_a_retry_is_removed(bool locked)
        {
            // The failed attempt created the file and couldn't delete it (as RealFileOps can't when its own delete is
            // denied too); the retry then only finds it there ("file exists"). It must still go.
            using (var t = new TestEnv())
            {
                byte[] stock = Fixtures.StockHostsBytes();
                t.WriteHosts(stock);
                t.Files.CreatePartialFault = n => n == 2 ? (locked ? (Exception)Faults.Sharing() : Faults.Denied()) : null;
                var w = new HostsWriter(t.Env);
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t, w).Kind);
                Assert.Equal(stock, t.ReadHosts());
                Assert.Equal(0, t.Files.HostsWrites);
                string original = Path.Combine(t.Location.Folder, Backups.OriginalName);
                Assert.False(File.Exists(original));
                Assert.Contains("backup: removed the partial copy " + original, w.Log);

                t.Files.CreatePartialFault = null;
                Assert.Equal(ResultKind.Success, ApplyBlock(t).Kind);
                Assert.Equal(stock, File.ReadAllBytes(original));
            }
        }

        [Fact]
        public void A_partial_backup_that_cant_be_removed_is_named_in_the_log()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHostsBytes());
                t.Files.CreatePartialFault = n => n == 2 ? Faults.Denied() : null;
                for (int i = 0; i < 3; i++) t.Files.DeleteFaults.Enqueue(Faults.Denied());   // the first try and both retries
                var w = new HostsWriter(t.Env);
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t, w).Kind);
                string original = Path.Combine(t.Location.Folder, Backups.OriginalName);
                Assert.Contains("partial backup left: " + original + " (UnauthorizedAccessException)", w.Log);
                Assert.Equal(0, t.Files.HostsWrites);
            }
        }

        [Fact]
        public void A_file_at_the_backup_path_with_other_bytes_is_never_deleted()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Fixtures.StockHostsBytes());
                // What ends up at the original backup's path isn't the start of the hosts file: not ours, so it stays.
                t.Files.TransformCreate = (n, b) => n == 2 ? B("# someone else's file\r\n") : b;
                t.Files.CreatePartialFault = n => n == 2 ? Faults.DiskFull() : null;
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.True(File.Exists(Path.Combine(t.Location.Folder, Backups.OriginalName)));
            }
        }

        [Fact]
        public void CreateNew_deletes_the_file_it_created_when_writing_fails()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Combine("x.bak");
                Assert.Throws<NullReferenceException>(() => RealFileOps.Instance.CreateNew(path, null!));   // after the file was created
                Assert.False(File.Exists(path));
            }
        }

        [Fact]
        public void A_rollback_whose_bytes_landed_before_the_flush_failed_counts_as_rolled_back()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.TransformWrite = (n, b) => n == 1 ? Corrupt(b) : b;   // the read-back matches neither version
                t.Files.FlushFailsAfterFirst = Faults.DiskFull();             // the rollback's bytes land, then the flush fails
                var w = new HostsWriter(t.Env);
                WriteOutcome o = ApplyBlock(t, w);
                Assert.Equal(ResultKind.Reverted, o.Kind);
                Assert.Equal("a\r\n", t.ReadHostsText());
                Assert.False(t.Machine.Read().HasPending);
                Assert.Contains(w.Log, l => l.StartsWith("rolled back to the previous file (the write reported IOException", StringComparison.Ordinal));
                Assert.DoesNotContain(w.Log, l => l.StartsWith("rollback failed", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void CreateNew_never_touches_a_file_that_exists()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Combine("x.bak");
                File.WriteAllText(path, "keep");
                Assert.ThrowsAny<IOException>(() => RealFileOps.Instance.CreateNew(path, new byte[] { 1, 2, 3 }));
                Assert.Equal("keep", File.ReadAllText(path));
            }
        }

        [Fact]
        public void A_change_that_would_pass_16_MB_is_refused_before_anything_is_written()
        {
            using (var t = new TestEnv())
            {
                var big = new byte[Limits.MaxHostsFileBytes - 100];
                for (int i = 0; i < big.Length; i++) big[i] = (byte)'#';
                big[big.Length - 2] = (byte)'\r';
                big[big.Length - 1] = (byte)'\n';
                t.WriteHosts(big);
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Refused, o.Kind);
                Assert.Equal(RefusalReason.TooLarge, o.Refusal);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Empty(t.BackupNames());
                Assert.False(t.Machine.Read().HasPending);
                Assert.Equal(ResultKind.Success, RemoveBlock(t).Kind);   // removing never grows the file
            }
        }

        [Fact]
        public void A_write_error_before_anything_is_written_is_Unexpected()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.WriteFaults.Enqueue(Faults.DiskFull());
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.Equal("a\r\n", t.ReadHostsText());
            }
        }

        [Fact]
        public void A_failing_read_back_is_Unexpected()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.BeforeHostsRead = n => { if (n == 3) for (int i = 0; i < 10; i++) t.Files.ReadFaults.Enqueue(Faults.DiskFull()); };
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.True(t.Machine.Read().HasPending);   // unverified: the marker stays
            }
        }

        [Fact]
        public void A_file_changed_meanwhile_is_redone_once()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.BeforeHostsRead = n => { if (n == 2) File.WriteAllText(t.HostsPath, "changed\r\n"); };
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Success, o.Kind);
                Assert.Equal(S(Apply(B("changed\r\n"))), t.ReadHostsText());
                Assert.Equal(Hashing.Sha256Hex(B("changed\r\n")), o.BeforeSha256);
            }
        }

        [Fact]
        public void A_file_that_keeps_changing_is_FileLocked()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.BeforeHostsRead = n =>
                {
                    if (n == 2) File.WriteAllText(t.HostsPath, "changed once\r\n");
                    if (n == 4) File.WriteAllText(t.HostsPath, "changed twice\r\n");
                };
                Assert.Equal(ResultKind.FileLocked, ApplyBlock(t).Kind);
                Assert.Equal("changed twice\r\n", t.ReadHostsText());
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.False(t.Machine.Read().HasPending);
            }
        }

        [Fact]
        public void Pending_is_set_during_the_write_and_cleared_after()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                MachineState? during = null;
                t.Files.TransformWrite = (n, b) => { during = t.Machine.Read(); return b; };
                ApplyBlock(t);
                Assert.NotNull(during);
                Assert.Equal(Hashing.Sha256Hex(B("a\r\n")), during!.PendingBefore);
                Assert.Equal(Hashing.Sha256Hex(t.ReadHosts()), during.PendingAfter);
                Assert.False(t.Machine.Read().HasPending);
            }
        }

        [Theory]
        [InlineData("after", "had been written")]
        [InlineData("before", "hadn't been written")]
        [InlineData("neither", "matched neither")]
        public void Recovery_clears_a_pending_marker(string fileMatches, string logged)
        {
            using (var t = new TestEnv())
            {
                byte[] before = B("before\r\n"), after = B("after\r\n");
                t.WriteHosts(fileMatches == "after" ? after : fileMatches == "before" ? before : B("other\r\n"));
                t.Machine.SetPending(Hashing.Sha256Hex(before), Hashing.Sha256Hex(after));
                var w = new HostsWriter(t.Env);
                w.Recover();
                Assert.False(t.Machine.Read().HasPending);
                Assert.Contains(w.Log, l => l.Contains(logged));
            }
        }

        [Fact]
        public void Recovery_keeps_the_marker_when_the_file_cant_be_read()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("x\r\n");
                t.Machine.SetPending(Hashing.Sha256Hex("a"), Hashing.Sha256Hex("b"));
                for (int i = 0; i < 10; i++) t.Files.ReadFaults.Enqueue(Faults.Sharing());
                var w = new HostsWriter(t.Env);
                w.Recover();
                Assert.True(t.Machine.Read().HasPending);
                Assert.Contains(w.Log, l => l.Contains("marker kept"));
            }
        }

        [Fact]
        public void Write_runs_recovery_first()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Machine.SetPending(Hashing.Sha256Hex("a"), Hashing.Sha256Hex("b"));
                var w = new HostsWriter(t.Env);
                Assert.Equal(ResultKind.Success, ApplyBlock(t, w).Kind);
                Assert.Contains(w.Log, l => l.StartsWith("recovery:", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void No_change_needed_writes_nothing()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(Apply(B("a\r\n")));
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Success, o.Kind);
                Assert.False(o.Written);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Empty(t.BackupNames());
                Assert.Equal(0, t.Flusher.Calls);
                Assert.Equal(0, t.Machine.WriteCount);
            }
        }

        [Fact]
        public void A_failed_flush_is_only_logged()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Flusher.Succeed = false;
                var w = new HostsWriter(t.Env);
                Assert.Equal(ResultKind.Success, ApplyBlock(t, w).Kind);
                Assert.Contains(w.Log, l => l.StartsWith("dns flush: failed", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void No_write_without_a_verified_backup()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.CreateFaults.Enqueue(Faults.DiskFull());
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Equal(0, t.Machine.WriteCount);
            }
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.Files.TransformCreate = (n, b) => Corrupt(B("backup content\r\n"));
                Assert.Equal(ResultKind.Unexpected, ApplyBlock(t).Kind);
                Assert.Equal(0, t.Files.HostsWrites);
                Assert.Equal("a\r\n", t.ReadHostsText());
            }
        }

        [Fact]
        public void Refusals_write_nothing()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts(new byte[] { 0x23, 0x00, 0x0D, 0x0A });
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(ResultKind.Refused, o.Kind);
                Assert.Equal(RefusalReason.UnsupportedEncoding, o.Refusal);
                Assert.Empty(t.BackupNames());
            }
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                t.DatabasePath.Value = @"D:\elsewhere";
                WriteOutcome o = ApplyBlock(t);
                Assert.Equal(RefusalReason.HostsPathMoved, o.Refusal);
                Assert.Equal(0, t.Files.HostsReads);
            }
            using (var t = new TestEnv())
            {
                using (var f = new FileStream(t.HostsPath, FileMode.Create)) f.SetLength(Limits.MaxHostsFileBytes + 1);
                Assert.Equal(RefusalReason.TooLarge, ApplyBlock(t).Refusal);
                Assert.Equal(0, t.Files.HostsWrites);
            }
        }

        [Fact]
        public void A_throwing_transform_is_Unexpected()
        {
            using (var t = new TestEnv())
            {
                t.WriteHosts("a\r\n");
                WriteOutcome o = new HostsWriter(t.Env).Write(doc => doc.Apply(Fixtures.Content(Level.Standard, "www.microsoft.com"), Guard.BuiltIn));
                Assert.Equal(ResultKind.Unexpected, o.Kind);
                Assert.IsType<InvalidOperationException>(o.Error);
                Assert.Equal(0, t.Files.HostsWrites);
            }
        }
    }
}
