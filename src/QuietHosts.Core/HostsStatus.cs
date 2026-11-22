using System;
using System.Collections.Generic;

namespace QuietHosts.Core
{
    /// <summary>
    /// Everything the window shows about the hosts file and the saved state, read without admin rights
    /// (<see cref="StatusReader.Read"/>). Init-only, so --render-ui can build fake statuses.
    /// </summary>
    public sealed class HostsStatus
    {
        public ProtectionState State { get; init; }
        /// <summary>Why the file can't be changed; None unless State is Refused.</summary>
        public RefusalReason Refusal { get; init; }
        public HostsWarnings Warnings { get; init; }

        /// <summary>The hosts file path.</summary>
        public string HostsPath { get; init; } = string.Empty;
        /// <summary>The hosts file's folder, where the backups are ("Open backup folder").</summary>
        public string HostsFolder { get; init; } = string.Empty;
        /// <summary>For Refused/HostsPathMoved: the folder Windows is set to use ({folder}).</summary>
        public string? MovedFolder { get; init; }
        /// <summary>For CantRead: what went wrong (for Copy details).</summary>
        public Exception? ReadError { get; init; }
        /// <summary>False when the hosts file is missing (it counts as empty).</summary>
        public bool FileExists { get; init; }
        /// <summary>File size in bytes.</summary>
        public long FileSize { get; init; }
        /// <summary>Bytes outside the managed blocks (the LargeFile warning's {size}).</summary>
        public long BytesOutsideBlocks { get; init; }
        /// <summary>SHA-256 hex of the bytes read, or null if the file couldn't be read. Compare before and after a change.</summary>
        public string? FileSha256 { get; init; }

        /// <summary>On: the count from the block's header ({count}); 0 otherwise.</summary>
        public int BlockedCount { get; init; }
        /// <summary>On: the level from the block's header (falls back to the saved level if the header is damaged).</summary>
        public Level? BlockLevel { get; init; }
        /// <summary>On: the list version in the block's header, "YYYY-MM-DD".</summary>
        public string? BlockListVersion { get; init; }
        /// <summary>The addresses in QuietHosts' block (the Fix dialog's "Blocked" rows); empty when not On.</summary>
        public IReadOnlyCollection<string> BlockedHosts { get; init; } = Array.Empty<string>();
        /// <summary>The first address in the block: the one the self-check looks up. Null when not On.</summary>
        public string? FirstBlockedHost { get; init; }

        /// <summary>OlderList and LegacyBlockToo: the "0.0.0.0" lines in the block-ads.ps1 block ({count}).</summary>
        public int LegacyCount { get; init; }
        public bool HasLegacyBlock { get; init; }

        /// <summary>Enabled=1 in the per-PC store.</summary>
        public bool SavedEnabled { get; init; }
        /// <summary>The saved level (Standard if none is saved).</summary>
        public Level SavedLevel { get; init; }
        /// <summary>The per-user level chosen while off, if any.</summary>
        public Level? PendingLevel { get; init; }
        /// <summary>
        /// The level the window's level cards show as "In use" or "Selected": the block's level when On, otherwise the
        /// pending level, otherwise the saved level, otherwise Standard. Turn on uses it.
        /// </summary>
        public Level SelectedLevel { get; init; }

        /// <summary>The allowlist, pruned to addresses in the lists.</summary>
        public IReadOnlyList<string> Allowlist { get; init; } = Array.Empty<string>();
        /// <summary>{k} in "You allowed {k}." and the Fix dialog's checkbox: allowed addresses that are in the lists.</summary>
        public int AllowedCount => Allowlist.Count;
        /// <summary>Allowed addresses that the selected level would otherwise block.</summary>
        public int AllowedInLevelCount { get; init; }

        /// <summary>"Undo all changes" has something to do: a block, the older block, damaged markers or saved settings.</summary>
        public bool CanUndo { get; init; }
        /// <summary>The per-user first-run flag.</summary>
        public bool FirstRunDone { get; init; }
        /// <summary>Details of the last elevated run, for Copy details.</summary>
        public string? LastRun { get; init; }
        /// <summary>A pending marker exists (a write is in progress or didn't finish).</summary>
        public bool HasPendingMarker { get; init; }
        /// <summary>An elevated run holds the write mutex right now.</summary>
        public bool RunInProgress { get; init; }
        public bool IsSandbox { get; init; }

        /// <summary>
        /// Lowercase hex SHA-256 of the Latin-1 bytes of every line strictly between QuietHosts' begin and end markers
        /// (without line endings), joined with "\n"; null without a well-formed block. A fingerprint for the
        /// "changed outside QuietHosts" notice: it changes whenever any line of the block does.
        /// </summary>
        public string? BlockLinesSha256 { get; init; }
        /// <summary>
        /// The lines that raise <see cref="HostsWarnings.MicrosoftLinesOutside"/>, in file order, trailing blanks removed,
        /// each cut to <see cref="HostsDocument.MaxMicrosoftLineLength"/> characters, at most
        /// <see cref="HostsDocument.MaxMicrosoftLinesShown"/>. Shown in the window only; never written anywhere.
        /// </summary>
        public IReadOnlyList<string> MicrosoftLines { get; init; } = Array.Empty<string>();
        /// <summary>How many lines raise MicrosoftLinesOutside (not capped).</summary>
        public int MicrosoftLineCount { get; init; }
        /// <summary>
        /// Lowercase hex SHA-256 of the Latin-1 bytes of all those lines (whole lines, trailing blanks removed, not capped),
        /// joined with "\n"; null when there are none. A fingerprint for the Microsoft-lines notice.
        /// </summary>
        public string? MicrosoftLinesSha256 { get; init; }

        public bool Has(HostsWarnings warning) => warning != HostsWarnings.None && (Warnings & warning) == warning;
    }
}
