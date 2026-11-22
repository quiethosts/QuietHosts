using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace QuietHosts.Core
{
    /// <summary>
    /// Per-PC state in the registry. The real one is HKLM\SOFTWARE\QuietHosts in the 64-bit view
    /// (<see cref="LocalMachine"/>); tests pass a temporary HKCU key instead.
    /// </summary>
    public sealed class RegistryMachineStore : IMachineStore
    {
        public const string MachineSubKey = @"SOFTWARE\QuietHosts";
        private readonly RegistryKey _baseKey;
        private readonly string _subKey;

        /// <param name="baseKey">An open base key, for example Registry.CurrentUser in tests. It is not disposed.
        /// HKLM is accepted only inside QuietHosts.exe, so tests and tools (and admin CI runners) can't write it.</param>
        /// <param name="subKey">The key path under it, for example @"Software\QuietHosts.Tests\&lt;guid&gt;".</param>
        public RegistryMachineStore(RegistryKey baseKey, string subKey)
        {
            _baseKey = baseKey ?? throw new ArgumentNullException(nameof(baseKey));
            if (string.IsNullOrWhiteSpace(subKey)) throw new ArgumentException("A subkey is needed.", nameof(subKey));
            if (baseKey.Name.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) && !HostsLocation.InsideQuietHostsExe())
                throw new InvalidOperationException("HKEY_LOCAL_MACHINE is only used inside QuietHosts.exe.");
            _subKey = subKey;
        }

        /// <summary>HKLM\SOFTWARE\QuietHosts, opened with RegistryView.Registry64.</summary>
        public static RegistryMachineStore LocalMachine() =>
            new RegistryMachineStore(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), MachineSubKey);

        public MachineState Read()
        {
            using (RegistryKey? key = _baseKey.OpenSubKey(_subKey, false))
            {
                if (key == null) return MachineState.Empty;
                string? levelText = key.GetValue("Level") as string;
                bool hasLevel = LevelNames.TryParse(levelText, out Level level);
                return new MachineState
                {
                    Exists = true,
                    Enabled = key.GetValue("Enabled") is int e && e == 1,
                    Level = level,
                    HasLevel = hasLevel,
                    Allowlist = MachineState.CleanAllowlist(key.GetValue("Allowlist") as string[]),
                    PendingBefore = MachineState.CleanHash(key.GetValue("PendingBefore") as string),
                    PendingAfter = MachineState.CleanHash(key.GetValue("PendingAfter") as string),
                    LastRun = key.GetValue("LastRun") as string,
                };
            }
        }

        public void SetEnabled(bool enabled) => Write(k => k.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord));
        public void SetLevel(Level level) => Write(k => k.SetValue("Level", LevelNames.ToKeyword(level), RegistryValueKind.String));
        public void SetAllowlist(IEnumerable<string> hosts) =>
            Write(k => k.SetValue("Allowlist", MachineState.CleanAllowlist(hosts).ToArray(), RegistryValueKind.MultiString));

        public void SetPending(string beforeSha256, string afterSha256) => Write(k =>
        {
            k.SetValue("PendingBefore", MachineState.CleanHash(beforeSha256) ?? string.Empty, RegistryValueKind.String);
            k.SetValue("PendingAfter", MachineState.CleanHash(afterSha256) ?? string.Empty, RegistryValueKind.String);
        });

        public void ClearPending()
        {
            using (RegistryKey? key = _baseKey.OpenSubKey(_subKey, true))
            {
                if (key == null) return;
                key.DeleteValue("PendingBefore", false);
                key.DeleteValue("PendingAfter", false);
            }
        }

        public void SetLastRun(string text) => Write(k => k.SetValue("LastRun", LastRunText.Cap(text), RegistryValueKind.String));

        public void DeleteAll() => _baseKey.DeleteSubKeyTree(_subKey, false);

        private void Write(Action<RegistryKey> action)
        {
            using (RegistryKey key = _baseKey.CreateSubKey(_subKey, true))
            {
                action(key);
            }
        }
    }

    /// <summary>Per-user settings in HKCU\Software\QuietHosts (or another key in tests).</summary>
    public sealed class RegistryUserStore : IUserStore
    {
        public const string UserSubKey = @"Software\QuietHosts";
        private readonly RegistryKey _baseKey;
        private readonly string _subKey;

        public RegistryUserStore(RegistryKey baseKey, string subKey)
        {
            _baseKey = baseKey ?? throw new ArgumentNullException(nameof(baseKey));
            if (string.IsNullOrWhiteSpace(subKey)) throw new ArgumentException("A subkey is needed.", nameof(subKey));
            _subKey = subKey;
        }

        /// <summary>HKCU\Software\QuietHosts.</summary>
        public static RegistryUserStore CurrentUser() => new RegistryUserStore(Registry.CurrentUser, UserSubKey);

        public UserSettings Read()
        {
            using (RegistryKey? key = _baseKey.OpenSubKey(_subKey, false))
            {
                if (key == null) return UserSettings.Empty;
                return new UserSettings
                {
                    Exists = true,
                    FirstRunDone = key.GetValue("FirstRunDone") is int f && f == 1,
                    PendingLevel = LevelNames.TryParse(key.GetValue("PendingLevel") as string, out Level l) ? l : (Level?)null,
                    MainX = key.GetValue("MainX") as int?,
                    MainY = key.GetValue("MainY") as int?,
                    FixW = key.GetValue("FixW") as int?,
                    FixH = key.GetValue("FixH") as int?,
                    // REG_MULTI_SZ only: any other type reads as nothing dismissed.
                    DismissedNotices = NoticeDismissalText.Clean(key.GetValue(DismissedNoticesValue) as string[]),
                };
            }
        }

        public const string DismissedNoticesValue = "DismissedNotices";

        public void SetDismissedNotices(IEnumerable<string> entries)
        {
            List<string> clean = NoticeDismissalText.Clean(entries);
            if (clean.Count > 0)
            {
                Write(k => k.SetValue(DismissedNoticesValue, clean.ToArray(), RegistryValueKind.MultiString));
                return;
            }
            // Nothing left: delete the value, without creating the key for it.
            using (RegistryKey? key = _baseKey.OpenSubKey(_subKey, true))
            {
                key?.DeleteValue(DismissedNoticesValue, false);
            }
        }

        public void SetFirstRunDone(bool done) => Write(k => k.SetValue("FirstRunDone", done ? 1 : 0, RegistryValueKind.DWord));

        public void SetPendingLevel(Level? level) => Write(k =>
        {
            if (level.HasValue) k.SetValue("PendingLevel", LevelNames.ToKeyword(level.Value), RegistryValueKind.String);
            else k.DeleteValue("PendingLevel", false);
        });

        public void SetMainPosition(int x, int y) => Write(k =>
        {
            k.SetValue("MainX", x, RegistryValueKind.DWord);
            k.SetValue("MainY", y, RegistryValueKind.DWord);
        });

        public void SetFixSize(int width, int height) => Write(k =>
        {
            k.SetValue("FixW", width, RegistryValueKind.DWord);
            k.SetValue("FixH", height, RegistryValueKind.DWord);
        });

        public void DeleteAll() => _baseKey.DeleteSubKeyTree(_subKey, false);

        private void Write(Action<RegistryKey> action)
        {
            using (RegistryKey key = _baseKey.CreateSubKey(_subKey, true))
            {
                action(key);
            }
        }
    }
}
