using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    /// <summary>The guards that keep tests (and CI runners, which run as admin) away from the real system.</summary>
    public class LocationGuardTests
    {
        [Fact]
        public void System_location_throws_outside_QuietHosts_exe()
        {
            Assert.Throws<InvalidOperationException>(() => HostsLocation.System());
            Assert.Throws<InvalidOperationException>(() => AppEnvironment.Real());
        }

        [Fact]
        public void The_real_path_is_only_computed_for_comparison()
        {
            Assert.EndsWith(@"\drivers\etc\hosts", HostsLocation.RealHostsPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(HostsLocation.IsRealHostsTarget(HostsLocation.RealHostsPath));
            Assert.True(HostsLocation.IsRealHostsTarget(HostsLocation.RealHostsPath.ToUpperInvariant()));
            Assert.True(HostsLocation.IsRealHostsTarget(Path.Combine(Path.GetDirectoryName(HostsLocation.RealHostsPath)!, "hosts.quiethosts-original.bak")));
            using (var dir = new TempDir())
            {
                Assert.False(HostsLocation.IsRealHostsTarget(dir.Combine("etc", "hosts")));
            }
        }

        [Fact]
        public void ForTesting_accepts_folders_under_temp()
        {
            using (var dir = new TempDir())
            {
                HostsLocation loc = HostsLocation.ForTesting(dir.Path);
                Assert.Equal(LocationKind.Testing, loc.Kind);
                Assert.Equal(Path.Combine(dir.Path, "etc", "hosts"), loc.HostsPath);
                Assert.Equal(Path.Combine(dir.Path, "etc"), loc.Folder);
                Assert.False(loc.IsSystem);
            }
        }

        [Theory]
        [InlineData(@"C:\")]
        [InlineData(@"C:\QuietHosts-test")]
        [InlineData(@"C:\Windows\System32\drivers\etc")]
        [InlineData(@"C:\Windows\Temp\QuietHosts")]
        [InlineData("")]
        public void ForTesting_refuses_roots_outside_temp(string root) =>
            Assert.ThrowsAny<ArgumentException>(() => HostsLocation.ForTesting(root));

        [Fact]
        public void ForTesting_refuses_temp_itself_and_tricks()
        {
            string temp = Path.GetTempPath();
            Assert.Throws<ArgumentException>(() => HostsLocation.ForTesting(temp));
            Assert.Throws<ArgumentException>(() => HostsLocation.ForTesting(Path.Combine(temp, "..")));
            Assert.Throws<ArgumentException>(() => HostsLocation.ForTesting(Path.Combine(temp, "x", "..", "..", "y")));
            Assert.Throws<ArgumentException>(() => HostsLocation.ForTesting(temp.TrimEnd('\\') + "Evil"));
            Assert.Throws<ArgumentException>(() => HostsLocation.ForTesting(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }

        [Fact]
        public void Unchecked_locations_never_reach_the_real_hosts_file_or_its_folder()
        {
            string realFolder = Path.GetDirectoryName(HostsLocation.RealHostsPath)!;
            foreach (LocationKind kind in new[] { LocationKind.Testing, LocationKind.System, LocationKind.Sandbox })
            {
                Assert.Throws<ArgumentException>(() => HostsLocation.Unchecked(HostsLocation.RealHostsPath, kind));
                Assert.Throws<ArgumentException>(() => HostsLocation.Unchecked(HostsLocation.RealHostsPath.ToUpperInvariant(), kind));
                Assert.Throws<ArgumentException>(() => HostsLocation.Unchecked(Path.Combine(realFolder, "hosts.quiethosts-test"), kind));
            }
        }

        [Fact]
        public void The_writer_writes_the_real_hosts_file_only_in_the_real_environment_inside_QuietHosts_exe()
        {
            // The check itself, since no test can build a location for the real file any more.
            string real = HostsLocation.RealHostsPath;
            Assert.Throws<InvalidOperationException>(() => HostsWriter.CheckTarget(real, isReal: true, insideQuietHostsExe: false));
            Assert.Throws<InvalidOperationException>(() => HostsWriter.CheckTarget(real, isReal: false, insideQuietHostsExe: true));
            Assert.Throws<InvalidOperationException>(() => HostsWriter.CheckTarget(real, isReal: false, insideQuietHostsExe: false));
            HostsWriter.CheckTarget(real, isReal: true, insideQuietHostsExe: true);   // the app itself
            Assert.False(HostsLocation.InsideQuietHostsExe());                         // and this test host is not it
            using (var dir = new TempDir())
            {
                HostsWriter.CheckTarget(dir.Combine("etc", "hosts"), isReal: false, insideQuietHostsExe: false);
                HostsWriter.CheckTarget(dir.Combine("etc", "hosts"), isReal: true, insideQuietHostsExe: false);
            }
        }

        [Fact]
        public void The_machine_store_refuses_HKLM_outside_QuietHosts_exe()
        {
            // Constructors only: nothing is read or written.
            Assert.Throws<InvalidOperationException>(() => RegistryMachineStore.LocalMachine());
            using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
            {
                Assert.Throws<InvalidOperationException>(() => new RegistryMachineStore(hklm, @"SOFTWARE\QuietHosts.Tests"));
            }
            Assert.Throws<InvalidOperationException>(() => new RegistryMachineStore(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\QuietHosts"));
        }

        [Fact]
        public void The_writer_refuses_the_real_hosts_file_in_a_non_real_environment()
        {
            string windowsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "QuietHosts.Tests", "etc", "hosts");
            HostsLocation underWindows = HostsLocation.Unchecked(windowsFile, LocationKind.Testing);
            var env2 = new AppEnvironment(underWindows, new InMemoryMachineStore(), new InMemoryUserStore(), RealFileOps.Instance,
                NoDnsFlusher.Instance, new FakeClock(), new FixedHostsDatabasePath(null), @"Local\QuietHosts.Test." + Guid.NewGuid().ToString("N"),
                TimeSpan.FromMilliseconds(10), isReal: false, requiresElevation: false);
            Assert.Throws<InvalidOperationException>(() => new HostsWriter(env2));
        }

        [Fact]
        public void A_non_real_environment_cannot_use_the_system_location_kind()
        {
            HostsLocation fakeSystem = HostsLocation.Unchecked(Path.Combine(Path.GetTempPath(), "x", "etc", "hosts"), LocationKind.System);
            Assert.Throws<ArgumentException>(() => new AppEnvironment(fakeSystem, new InMemoryMachineStore(), new InMemoryUserStore(),
                RealFileOps.Instance, NoDnsFlusher.Instance, new FakeClock(), new FixedHostsDatabasePath(null), @"Local\x", TimeSpan.Zero,
                isReal: false, requiresElevation: false));
        }

        [Fact]
        public void A_junction_under_temp_is_resolved()
        {
            using (var dir = new TempDir())
            {
                string target = dir.Combine("target");
                Directory.CreateDirectory(target);
                string link = dir.Combine("link");
                var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c mklink /J \"" + link + "\" \"" + target + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi)!)
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return;   // junctions unavailable here; the path checks above still hold
                }
                try
                {
                    string? final = Native.FinalPath(link);
                    Assert.NotNull(final);
                    Assert.True(PathUtil.SamePath(final!, Native.FinalPath(target)!));
                    Assert.False(HostsLocation.IsRealHostsTarget(Path.Combine(link, "hosts")));
                }
                finally
                {
                    Directory.Delete(link);
                }
            }
        }

        [Fact]
        public void Moved_hosts_database_detection()
        {
            Assert.False(HostsDatabase.IsMoved(null));
            Assert.False(HostsDatabase.IsMoved(""));
            Assert.False(HostsDatabase.IsMoved(HostsDatabase.DefaultFolder));
            Assert.False(HostsDatabase.IsMoved(HostsDatabase.DefaultFolder.ToLowerInvariant() + @"\"));
            Assert.True(HostsDatabase.IsMoved(@"C:\Users\Public\etc"));
            Assert.True(HostsDatabase.IsMoved(HostsDatabase.DefaultFolder + @"\sub"));
            Assert.True(HostsDatabase.IsMoved(HostsDatabase.DefaultFolder.Replace('\\', '/')));
            Assert.True(HostsDatabase.IsMoved(@"%SystemDrive%\Windows\System32\drivers\etc"));   // not expanded: treated as moved

            // The default folder is the folder of the file QuietHosts writes.
            Assert.Equal(Path.GetDirectoryName(HostsLocation.RealHostsPath), HostsDatabase.DefaultFolder);
            Assert.False(HostsDatabase.IsMoved(HostsDatabase.Expand(@"%SystemRoot%\System32\drivers\etc")));
            Assert.False(HostsDatabase.IsMoved(HostsDatabase.Expand(@"%windir%\system32\DRIVERS\etc\")));
            Assert.False(HostsDatabase.IsMoved(HostsDatabase.Expand(@"%SYSTEMROOT%\System32\drivers\etc")));
        }

#if DEBUG
        [Fact]
        public void Sandbox_location_is_under_temp_and_seeded_once()
        {
            Assert.Equal(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "QuietHosts-sandbox"), HostsLocation.SandboxRoot);
            using (var dir = new TempDir())
            {
                // Seeded from a synthesized file here, never from this PC's hosts file.
                string seed = dir.Combine("seed-hosts");
                File.WriteAllBytes(seed, Fixtures.StockHostsBytes());
                AppEnvironment env = AppEnvironment.SandboxAt(dir.Combine("sandbox"), seed);
                Assert.True(env.IsSandbox);
                Assert.False(env.IsReal);
                Assert.False(env.RequiresElevation);
                Assert.IsType<NoDnsFlusher>(env.Flusher);
                Assert.IsType<FileMachineStore>(env.Machine);
                Assert.IsType<FileUserStore>(env.User);
                Assert.Equal(AppEnvironment.SandboxWriteMutexName, env.WriteMutexName);
                Assert.Equal(Fixtures.StockHostsBytes(), File.ReadAllBytes(env.Location.HostsPath));

                File.WriteAllText(env.Location.HostsPath, "changed\r\n");
                AppEnvironment.SandboxAt(dir.Combine("sandbox"), seed);
                Assert.Equal("changed\r\n", File.ReadAllText(env.Location.HostsPath));
                Assert.Throws<ArgumentException>(() => AppEnvironment.SandboxAt(@"C:\QuietHosts-sandbox", null));
            }
        }

        [Fact]
        public void Sandbox_elevated_run_uses_the_file_stores()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.SandboxAt(dir.Combine("sandbox"), null);
                File.WriteAllText(env.Location.HostsPath, "127.0.0.1 localhost\r\n");
                Assert.Equal(0, ElevatedMain.Run(ElevatedCommand.On(Level.Standard).WithSandbox(true), env, () => false));
                Assert.True(HostsDocument.Parse(File.ReadAllBytes(env.Location.HostsPath)).HasBlock);
                Assert.True(env.Machine.Read().Enabled);
                Assert.True(File.Exists(Path.Combine(dir.Combine("sandbox"), "machine.ini")));
                Assert.Equal(ProtectionState.On, StatusReader.Read(env).State);
                Assert.True(SelfCheck.Check(env, StatusReader.Read(env).FirstBlockedHost!).Passed);
                Assert.Equal(0, ElevatedMain.Run(ElevatedCommand.Undo().WithSandbox(true), env, () => false));
                Assert.False(env.Machine.Read().Exists);
            }
        }
#endif
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ProcessEnvironmentCollection
    {
        public const string Name = "Changes the process environment";
    }

    /// <summary>Changes this process's environment, so it runs alone.</summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public class ProcessEnvironmentTests
    {
        [Fact]
        public void The_moved_database_check_ignores_SystemRoot_and_windir_in_the_environment()
        {
            string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            string? windir = Environment.GetEnvironmentVariable("windir");
            string realDefault = HostsDatabase.DefaultFolder;
            try
            {
                Environment.SetEnvironmentVariable("SystemRoot", @"C:\Elsewhere");
                Environment.SetEnvironmentVariable("windir", @"C:\Elsewhere");
                Assert.Equal(realDefault, HostsDatabase.DefaultFolder);
                Assert.False(HostsDatabase.IsMoved(HostsDatabase.Expand(@"%SystemRoot%\System32\drivers\etc")));
                Assert.False(HostsDatabase.IsMoved(realDefault));
                Assert.True(HostsDatabase.IsMoved(@"C:\Elsewhere\System32\drivers\etc"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("SystemRoot", systemRoot);
                Environment.SetEnvironmentVariable("windir", windir);
            }
        }
    }

    public class StoreTests
    {
        [Fact]
        public void Registry_machine_store_round_trips_on_a_temp_HKCU_key()
        {
            using (var key = new TempRegistryKey())
            {
                var store = new RegistryMachineStore(key.Base, key.SubKey);
                Assert.False(store.Read().Exists);
                store.SetEnabled(true);
                store.SetLevel(Level.Strict);
                store.SetAllowlist(new[] { "b.ads-two.com", "A.ads-one.net", "not a host", "a.ads-one.net" });
                store.SetPending(Hashing.Sha256Hex("x"), Hashing.Sha256Hex("y"));
                store.SetLastRun(new string('z', 9000));
                MachineState s = store.Read();
                Assert.True(s.Exists);
                Assert.True(s.Enabled);
                Assert.Equal(Level.Strict, s.Level);
                Assert.True(s.HasLevel);
                Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com" }, s.Allowlist);
                Assert.Equal(Hashing.Sha256Hex("x"), s.PendingBefore);
                Assert.Equal(Hashing.Sha256Hex("y"), s.PendingAfter);
                Assert.Equal(Limits.MaxLastRunChars, s.LastRun!.Length);

                using (var raw = key.Base.OpenSubKey(key.SubKey)!)
                {
                    Assert.Equal(Microsoft.Win32.RegistryValueKind.DWord, raw.GetValueKind("Enabled"));
                    Assert.Equal(Microsoft.Win32.RegistryValueKind.String, raw.GetValueKind("Level"));
                    Assert.Equal(Microsoft.Win32.RegistryValueKind.MultiString, raw.GetValueKind("Allowlist"));
                    Assert.Equal("strict", raw.GetValue("Level"));
                }

                store.ClearPending();
                Assert.False(store.Read().HasPending);
                store.SetEnabled(false);
                Assert.False(store.Read().Enabled);
                store.DeleteAll();
                Assert.False(store.Read().Exists);
                store.DeleteAll();
            }
        }

        [Fact]
        public void Registry_machine_store_reads_bad_values_as_defaults()
        {
            using (var key = new TempRegistryKey())
            {
                using (var k = key.Base.CreateSubKey(key.SubKey))
                {
                    k.SetValue("Enabled", "1");                       // wrong type
                    k.SetValue("Level", "STRICT");
                    k.SetValue("Allowlist", "a.ads-one.net");         // not MULTI_SZ
                    k.SetValue("PendingBefore", "nothex");
                }
                MachineState s = new RegistryMachineStore(key.Base, key.SubKey).Read();
                Assert.True(s.Exists);
                Assert.False(s.Enabled);
                Assert.False(s.HasLevel);
                Assert.Equal(Level.Standard, s.Level);
                Assert.Empty(s.Allowlist);
                Assert.False(s.HasPending);
            }
        }

        [Fact]
        public void Allowlist_is_capped_at_2000()
        {
            var store = new InMemoryMachineStore();
            store.SetAllowlist(System.Linq.Enumerable.Range(0, 2500).Select(i => "h" + i + ".ads-one.net"));
            Assert.Equal(Limits.MaxAllowlist, store.Read().Allowlist.Count);
        }

        [Fact]
        public void Registry_user_store_round_trips()
        {
            using (var key = new TempRegistryKey())
            {
                var store = new RegistryUserStore(key.Base, key.SubKey);
                Assert.False(store.Read().Exists);
                store.SetFirstRunDone(true);
                store.SetPendingLevel(Level.Strict);
                store.SetMainPosition(-1200, 40);
                store.SetFixSize(760, 540);
                UserSettings s = store.Read();
                Assert.True(s.FirstRunDone);
                Assert.Equal(Level.Strict, s.PendingLevel);
                Assert.Equal(-1200, s.MainX);
                Assert.Equal(40, s.MainY);
                Assert.Equal(760, s.FixW);
                Assert.Equal(540, s.FixH);
                store.SetPendingLevel(null);
                Assert.Null(store.Read().PendingLevel);
                store.DeleteAll();
                Assert.False(store.Read().Exists);
            }
        }

        [Fact]
        public void File_stores_round_trip()
        {
            using (var dir = new TempDir())
            {
                var m = new FileMachineStore(dir.Combine("machine.ini"));
                Assert.False(m.Read().Exists);
                m.SetEnabled(true);
                m.SetLevel(Level.Strict);
                m.SetAllowlist(new[] { "b.ads-two.com", "a.ads-one.net" });
                m.SetPending(Hashing.Sha256Hex("x"), Hashing.Sha256Hex("y"));
                m.SetLastRun("line one\nline two \\ backslash\r\nthree=3");
                MachineState s = m.Read();
                Assert.True(s.Enabled);
                Assert.Equal(Level.Strict, s.Level);
                Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com" }, s.Allowlist);
                Assert.True(s.HasPending);
                Assert.Equal("line one\nline two \\ backslash\r\nthree=3", s.LastRun);
                m.ClearPending();
                Assert.False(m.Read().HasPending);
                m.DeleteAll();
                Assert.False(m.Read().Exists);

                var u = new FileUserStore(dir.Combine("user.ini"));
                u.SetFirstRunDone(true);
                u.SetPendingLevel(Level.Standard);
                u.SetMainPosition(10, -20);
                u.SetFixSize(800, 600);
                UserSettings us = u.Read();
                Assert.True(us.FirstRunDone);
                Assert.Equal(Level.Standard, us.PendingLevel);
                Assert.Equal(-20, us.MainY);
                Assert.Equal(600, us.FixH);
                u.DeleteAll();
                Assert.False(u.Read().Exists);
            }
        }

        [Fact]
        public void In_memory_user_store_round_trips()
        {
            var u = new InMemoryUserStore();
            Assert.False(u.Read().Exists);
            u.SetPendingLevel(Level.Strict);
            u.SetFirstRunDone(true);
            Assert.Equal(Level.Strict, u.Read().PendingLevel);
            u.SetPendingLevel(null);
            Assert.Null(u.Read().PendingLevel);
            Assert.True(u.Read().FirstRunDone);
            u.DeleteAll();
            Assert.False(u.Read().Exists);
        }

        // ---------------------------------------------------------------- dismissed notices (T-A-40..43)

        private static readonly string[] Dismissals =
        {
            "edited-block=" + new string('3', 64),
            "large-file=size:1480000",
            "newer-list-in-app=app:1.0.0|lists:2026-11-20",
            "downloads-tip=path:0123456789abcdef",
        };

        /// <summary>The three user stores, each on its own temporary place; the registry one under HKCU\Software\QuietHosts.Tests\&lt;guid&gt;.</summary>
        private static void WithEachUserStore(Action<IUserStore, string> test)
        {
            using (var key = new TempRegistryKey())
            {
                test(new RegistryUserStore(key.Base, key.SubKey), "registry");
            }
            using (var dir = new TempDir())
            {
                test(new FileUserStore(dir.Combine("user.ini")), "file");
            }
            test(new InMemoryUserStore(), "memory");
        }

        [Fact]
        public void Dismissed_notices_round_trip_in_every_user_store()
        {
            WithEachUserStore((store, name) =>
            {
                Assert.Empty(store.Read().DismissedNotices);
                store.SetDismissedNotices(Dismissals);
                Assert.True(Dismissals.SequenceEqual(store.Read().DismissedNotices), name);

                // A new dismissal of the same id replaces the old one.
                store.SetDismissedNotices(Dismissals.Concat(new[] { "large-file=size:2000000" }));
                Assert.Equal(new[] { Dismissals[0], "large-file=size:2000000", Dismissals[2], Dismissals[3] }, store.Read().DismissedNotices);

                store.SetDismissedNotices(Array.Empty<string>());
                Assert.Empty(store.Read().DismissedNotices);

                store.SetDismissedNotices(Dismissals);
                store.DeleteAll();
                Assert.Empty(store.Read().DismissedNotices);
                Assert.False(store.Read().Exists, name);
            });
        }

        [Fact]
        public void Clean_keeps_only_well_formed_entries_and_the_last_one_per_id()
        {
            Assert.Empty(NoticeDismissalText.Clean(null));
            List<string> clean = NoticeDismissalText.Clean(new[]
            {
                "edited-block=abc",
                "Edited-block=abc",              // uppercase id
                "1large=abc",                    // id starts with a digit
                "large-file=",                   // empty fingerprint
                "=abc",                          // no id
                "large-file size:1",             // no "="
                "large-file=size 1",             // a space in the fingerprint
                "large-file=size:1\n",           // a trailing line break ($ would let this through)
                "large-file=size:1\r",
                "large-file=café",          // not ASCII
                "a" + new string('b', 32) + "=x", // id of 33 characters
                "large-file=" + new string('f', 96), // fingerprint of 96 characters
                null!,
                "microsoft-lines=" + new string('a', 64),
                "edited-block=def",              // the last entry per id wins, in the first one's place
            });
            Assert.Equal(new[] { "edited-block=def", "microsoft-lines=" + new string('a', 64) }, clean);

            // Longest allowed: a 32-character id and a 95-character fingerprint, 128 characters in all.
            string longest = "a" + new string('b', 31) + "=" + new string('c', 95);
            Assert.Equal(NoticeDismissalText.MaxLength, longest.Length);
            Assert.Equal(new[] { longest }, NoticeDismissalText.Clean(new[] { longest, longest + "c" }));

            // At most 16 entries: the 17th id is dropped.
            List<string> many = Enumerable.Range(0, 17).Select(i => "id" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=x").ToList();
            List<string> capped = NoticeDismissalText.Clean(many);
            Assert.Equal(NoticeDismissalText.MaxEntries, capped.Count);
            Assert.Equal(many.Take(16), capped);
            Assert.DoesNotContain("id16=x", capped);
        }

        [Fact]
        public void Dismissed_notices_in_the_registry_are_a_multi_string_value_or_nothing()
        {
            using (var key = new TempRegistryKey())
            {
                var store = new RegistryUserStore(key.Base, key.SubKey);

                // An empty list creates nothing.
                store.SetDismissedNotices(Array.Empty<string>());
                Assert.Null(key.Base.OpenSubKey(key.SubKey));

                store.SetDismissedNotices(Dismissals);
                using (var raw = key.Base.OpenSubKey(key.SubKey)!)
                {
                    Assert.Equal(Microsoft.Win32.RegistryValueKind.MultiString, raw.GetValueKind(RegistryUserStore.DismissedNoticesValue));
                }

                // An empty list deletes the value (the key stays for the other settings).
                store.SetFirstRunDone(true);
                store.SetDismissedNotices(new[] { "not an entry" });
                using (var raw = key.Base.OpenSubKey(key.SubKey)!)
                {
                    Assert.DoesNotContain(RegistryUserStore.DismissedNoticesValue, raw.GetValueNames());
                }
                Assert.True(store.Read().FirstRunDone);

                // Another value type reads as nothing dismissed.
                using (var k = key.Base.CreateSubKey(key.SubKey))
                {
                    k.SetValue(RegistryUserStore.DismissedNoticesValue, "edited-block=abc", Microsoft.Win32.RegistryValueKind.String);
                }
                Assert.Empty(store.Read().DismissedNotices);

                // Bad entries in the value are skipped on read.
                using (var k = key.Base.CreateSubKey(key.SubKey))
                {
                    k.SetValue(RegistryUserStore.DismissedNoticesValue, new[] { "bad entry", "edited-block=abc", "x=" }, Microsoft.Win32.RegistryValueKind.MultiString);
                }
                Assert.Equal(new[] { "edited-block=abc" }, store.Read().DismissedNotices);

                store.DeleteAll();
                Assert.Null(key.Base.OpenSubKey(key.SubKey));
            }
        }

        [Fact]
        public void Dismissals_and_the_other_user_settings_never_touch_each_other()
        {
            WithEachUserStore((store, name) =>
            {
                store.SetFirstRunDone(true);
                store.SetPendingLevel(Level.Strict);
                store.SetMainPosition(-1200, 40);
                store.SetFixSize(760, 540);
                store.SetDismissedNotices(Dismissals);

                UserSettings s = store.Read();
                Assert.True(s.FirstRunDone, name);
                Assert.Equal(Level.Strict, s.PendingLevel);
                Assert.Equal(-1200, s.MainX);
                Assert.Equal(40, s.MainY);
                Assert.Equal(760, s.FixW);
                Assert.Equal(540, s.FixH);

                // Every other setter keeps the dismissals.
                store.SetFirstRunDone(false);
                Assert.Equal(Dismissals, store.Read().DismissedNotices);
                store.SetPendingLevel(null);
                Assert.Equal(Dismissals, store.Read().DismissedNotices);
                store.SetPendingLevel(Level.Standard);
                Assert.Equal(Dismissals, store.Read().DismissedNotices);
                store.SetMainPosition(10, 20);
                Assert.Equal(Dismissals, store.Read().DismissedNotices);
                store.SetFixSize(800, 600);
                Assert.Equal(Dismissals, store.Read().DismissedNotices);

                // And clearing the dismissals keeps the rest.
                store.SetDismissedNotices(Array.Empty<string>());
                s = store.Read();
                Assert.False(s.FirstRunDone, name);
                Assert.Equal(Level.Standard, s.PendingLevel);
                Assert.Equal(10, s.MainX);
                Assert.Equal(20, s.MainY);
                Assert.Equal(800, s.FixW);
                Assert.Equal(600, s.FixH);
                Assert.Empty(s.DismissedNotices);
            });
        }
    }
}
