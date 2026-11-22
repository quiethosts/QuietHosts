using System;

namespace QuietHosts.Core
{
    // The window words every value of these enums (Strings.cs), and a test pins their names and
    // numbers. Don't rename, renumber or add values without updating the window and that test.

    /// <summary>The blocking level the user picks. Standard writes tier Default; Strict writes Default and Strict.</summary>
    public enum Level
    {
        Standard,
        Strict,
    }

    /// <summary>The list file an address comes from: lists/default.txt or lists/strict.txt.</summary>
    public enum Tier
    {
        Default,
        Strict,
    }

    /// <summary>The main state the window shows.</summary>
    public enum ProtectionState
    {
        /// <summary>No QuietHosts block, nothing saved as on, no older block.</summary>
        Off,
        /// <summary>The QuietHosts block is in the hosts file.</summary>
        On,
        /// <summary>Saved as on (Enabled=1), but the block is missing from the hosts file.</summary>
        Removed,
        /// <summary>Only the older block-ads.ps1 block is present.</summary>
        OlderList,
        /// <summary>The hosts file could not be read.</summary>
        CantRead,
        /// <summary>The file can't safely be changed; see <see cref="RefusalReason"/>.</summary>
        Refused,
    }

    /// <summary>Why the hosts file can't safely be changed. None unless the state is Refused.</summary>
    public enum RefusalReason
    {
        None,
        /// <summary>The registry's DataBasePath points to another folder (a malware sign).</summary>
        HostsPathMoved,
        /// <summary>A UTF-16 or UTF-32 byte order mark, or a NUL byte.</summary>
        UnsupportedEncoding,
        /// <summary>The file is over 16 MB.</summary>
        TooLarge,
    }

    /// <summary>Extra lines under the detail line; several can apply at once.</summary>
    [Flags]
    public enum HostsWarnings
    {
        None = 0,
        /// <summary>The block's entry lines don't match the header's sha256, or the header is damaged.</summary>
        EditedBlock = 1,
        /// <summary>Unpaired, duplicated or nested marker lines; repaired at the next change.</summary>
        MalformedMarkers = 2,
        /// <summary>The bytes outside the managed blocks are over 256 KB.</summary>
        LargeFile = 4,
        /// <summary>An active line outside the managed blocks maps a Microsoft name (Defender may reset the file).</summary>
        MicrosoftLinesOutside = 8,
        /// <summary>A write was marked pending, and no elevated run is in progress.</summary>
        ChangeNotFinished = 16,
        /// <summary>The older block-ads.ps1 block is present next to QuietHosts' own block (or the Removed state).</summary>
        LegacyBlockToo = 32,
    }

    /// <summary>The outcome of one change.</summary>
    public enum ResultKind
    {
        Success,
        /// <summary>The user said No to the Windows prompt (Win32Exception 1223). Window side only.</summary>
        Declined,
        AccessDenied,
        FileLocked,
        Reverted,
        Busy,
        /// <summary>The file can't safely be changed (exit code 16).</summary>
        Refused,
        Unexpected,
    }

    /// <summary>The verbs of an elevated run.</summary>
    public enum Verb
    {
        On,
        Off,
        Allow,
        Block,
        Undo,
    }

    /// <summary>Exit codes of the elevated run. 14 stays reserved and unused.</summary>
    public static class ExitCodes
    {
        public const int Success = 0;
        public const int AccessDenied = 10;
        public const int FileLocked = 11;
        public const int Reverted = 12;
        public const int Busy = 13;
        public const int Unexpected = 15;
        public const int Refused = 16;

        /// <summary>The exit code for a result. Declined has no exit code (it never reaches the child) and maps to Unexpected.</summary>
        public static int FromResult(ResultKind kind)
        {
            switch (kind)
            {
                case ResultKind.Success: return Success;
                case ResultKind.AccessDenied: return AccessDenied;
                case ResultKind.FileLocked: return FileLocked;
                case ResultKind.Reverted: return Reverted;
                case ResultKind.Busy: return Busy;
                case ResultKind.Refused: return Refused;
                default: return Unexpected;
            }
        }

        /// <summary>The result for an exit code. Any unknown code (including 14) means Unexpected.</summary>
        public static ResultKind ToResult(int exitCode)
        {
            switch (exitCode)
            {
                case Success: return ResultKind.Success;
                case AccessDenied: return ResultKind.AccessDenied;
                case FileLocked: return ResultKind.FileLocked;
                case Reverted: return ResultKind.Reverted;
                case Busy: return ResultKind.Busy;
                case Refused: return ResultKind.Refused;
                default: return ResultKind.Unexpected;
            }
        }
    }

    /// <summary>Lowercase words used in the registry, the block header and command lines.</summary>
    public static class LevelNames
    {
        public const string Standard = "standard";
        public const string Strict = "strict";

        public static string ToKeyword(Level level) => level == Level.Strict ? Strict : Standard;

        /// <summary>Parses "standard" or "strict" exactly (ordinal, lowercase only).</summary>
        public static bool TryParse(string? text, out Level level)
        {
            if (string.Equals(text, Standard, StringComparison.Ordinal)) { level = Level.Standard; return true; }
            if (string.Equals(text, Strict, StringComparison.Ordinal)) { level = Level.Strict; return true; }
            level = Level.Standard;
            return false;
        }
    }
}
