# QuietHosts.Core: guide for the window

Everything except WinForms lives in `QuietHosts.Core` (one namespace). The exe compiles these
sources in, so the window calls them directly. All of it targets net48, culture-free (ordinal
comparisons, `ToLowerInvariant`, InvariantCulture). Nothing here shows UI text except the
category words (`Categories`); every other string the user sees belongs in `Strings.cs`.

## 1. Program.Main

```csharp
[STAThread]
static int Main(string[] args)
{
    // First, before any WinForms initialization:
    if (ElevatedCommand.IsElevatedInvocation(args)) return ElevatedMain.Run(args);
    // ... single instance, then the window
}
```

`ElevatedMain.Run` calls `SetDefaultDllDirectories(System32)`, parses strictly, checks elevation
(except in the Debug sandbox), takes `Global\QuietHosts.Write` (5 s, else Busy), recovers a pending
marker, runs the verb, writes `LastRun` and returns the exit code. It never shows UI. A command line
that doesn't parse exits 15 and changes nothing; an elevated run still writes `LastRun` with the reason
and the argument count and length (never the arguments), for example
`command: rejected: missing --end (57 arguments, 1402 characters)`.

## 2. Environments

| Call | Use |
|---|---|
| `AppEnvironment.Real()` | The app on this PC: real hosts file, `HKLM\SOFTWARE\QuietHosts` (64-bit view, read by the window), `HKCU\Software\QuietHosts`. Throws unless the entry assembly is `QuietHosts`. |
| `AppEnvironment.Sandbox()` | `#if DEBUG` only. `%TEMP%\QuietHosts-sandbox\etc\hosts` (seeded once as a byte copy of the real file), `machine.ini` / `user.ini` in that folder, no DNS flush, no elevation. |
| `AppEnvironment.ForTesting(root)` / the public constructor | Tests and `--render-ui` fakes. Roots must be under `%TEMP%`. |

`env.IsSandbox` is true for the sandbox: put "(sandbox)" in the title. The window decides
Real vs Sandbox from its own `--sandbox` argument (Debug builds only).

## 3. Lists and counts

```csharp
BlockLists lists = BlockLists.LoadEmbedded();   // once at startup; immutable, thread-safe to read
```

| Member | Meaning |
|---|---|
| `StandardCount` | {n} addresses in Standard (tier Default) |
| `StrictExtraCount` | {m} more in Strict (tier Strict) |
| `StrictTotalCount`, `CountFor(level)` | everything a level's tiers hold |
| `Version`, `Date` | "2026-11-20" and the date (format "d MMM yyyy", InvariantCulture) |
| `Entries`, `DefaultEntries`, `StrictEntries`, `Find(host)`, `Contains(host)` | `ListEntry`: `Host`, `Tier`, `Category` (key), `CategoryWords` ("What it is"), `Company` ("" if none), `Note` ("" = "None known") |
| `GuardedDroppedCount`, `Issues` | diagnostics; the lint tests keep the shipped lists clean |

`Categories.Words(key)` gives the UI words for a category key (unknown keys: "Ads or tracking").
Never hardcode a count: compute it from `lists` (or, for "Blocking {count}", from the status).

## 4. Status

```csharp
HostsStatus s = StatusReader.Read(env, lists);   // no admin rights; never writes; may wait up to ~0.6 s on a locked file
```

Re-read after every result, on F5, and after "Check again" / "Try again".

| `s.State` | When | Main button |
|---|---|---|
| `Off` | no QuietHosts block | Turn on protection |
| `On` | the block is in the file | Turn off protection |
| `Removed` | `Enabled=1` but the block is gone | Turn on again |
| `OlderList` | only the block-ads.ps1 block | Turn on protection |
| `CantRead` | the file can't be read (`s.ReadError`) | Try again (reads the file again) |
| `Refused` | see `s.Refusal` | Check again (reads the file again) |

| `s.Refusal` | Detail needs |
|---|---|
| `None` | not refused |
| `HostsPathMoved` | `s.MovedFolder` ({folder}) |
| `UnsupportedEncoding` | UTF-16/32 BOM or a NUL byte |
| `TooLarge` | over 16 MB |

`s.Warnings` is `[Flags]`; test with `s.Has(HostsWarnings.X)`. Several can show at once:
`EditedBlock`, `MalformedMarkers`, `LargeFile` (size: `s.BytesOutsideBlocks`),
`MicrosoftLinesOutside`, `ChangeNotFinished`, `LegacyBlockToo`.
`MicrosoftLinesOutside` means another program's active line (outside our block, or one that moves out of it at the
next write) maps one of Microsoft's own service names, the ones Defender is reported to check
(`Guard.IsDefenderSensitive`: microsoft.com, windows.com, windowsupdate.com, live.com, msn.com, bing.com, office.com,
outlook.com, xbox.com, login.windows.net, the .microsoft TLD and similar). It is narrower than what QuietHosts never
writes: other programs' lines for adnxs.com, clarity.ms, LinkedIn, GitHub, the game companies or Azure customer
hosting (azurewebsites.net, blob.core.windows.net) don't raise it.

Other fields: `BlockedCount` ({count}, from the block header), `BlockLevel`, `BlockListVersion`,
`SelectedLevel` (what the level cards show as "In use" or "Selected": the block's level when On, else the per-user
pending level, else the saved level, else Standard), `SavedEnabled`, `SavedLevel`, `PendingLevel`,
`Allowlist` / `AllowedCount` ({k}; pruned to the lists), `AllowedInLevelCount`, `LegacyCount`
({count} for OlderList), `HasLegacyBlock` (first-run sentence, Undo sentence), `CanUndo`
(enable "Undo all changes"), `FirstRunDone`, `FirstBlockedHost` (for the self-check),
`HostsFolder` ("Open backup folder"), `FileSha256`, `LastRun`, `IsSandbox`.
`BlockedHosts` holds the entries of our block in the file, also those another program rewrote (a tab for the space,
trailing blanks or doubled CRs): they still block, and the next change writes them back exactly (EditedBlock shows
meanwhile).

Fingerprints and lines for the window's notices (read-only; never written to LastRun, Copy details or the
registry). Can't read and Refused statuses keep the defaults.

| Member | Meaning | Default |
|---|---|---|
| `BlockLinesSha256` | lowercase hex SHA-256 of the Latin-1 bytes of every line strictly between our begin and end markers (without line endings), joined with "\n": changes whenever any line of the block does (the "changed outside QuietHosts" notice) | null without a well-formed block |
| `MicrosoftLines` | the lines that raise `MicrosoftLinesOutside` (role outside or foreign, mapping a `Guard.IsDefenderSensitive` name), in file order, trailing blanks removed, each cut to `HostsDocument.MaxMicrosoftLineLength` (200) characters, at most `HostsDocument.MaxMicrosoftLinesShown` (50) | empty |
| `MicrosoftLineCount` | how many such lines there are (not capped) | 0 |
| `MicrosoftLinesSha256` | lowercase hex SHA-256 of the Latin-1 bytes of all those lines (whole lines, trailing blanks removed, not capped) joined with "\n" | null when there are none |

`RunInProgress` is true while an elevated QuietHosts run has the write mutex (`env.WriteMutexName`,
`Global\QuietHosts.Write`) open: from its start until it
exits, which in practice is while it waits for the mutex (at most 5 s) or holds it. The window never opens or creates
the mutex itself, so `RunInProgress` at start-up or after F5 means a change started earlier (from this window or
another one) is still running; `ChangeNotFinished` is held back meanwhile. `HasPendingMarker` is true while the
`PendingBefore`/`PendingAfter` checksums exist (a write is running, or one didn't finish); `ChangeNotFinished` is
`HasPendingMarker` without `RunInProgress`.

The exe's path: `typeof(Program).Assembly.Location`, the file system path. Not `Application.ExecutablePath`: it goes
through a file URI and decodes `%41`-style sequences in folder names, so it can name a file that isn't there.

Run-location tips: `RunLocation.Check(exePath)` returns
`RunLocationTips.RunningFromDownloads` and/or `RunningFromTemp`.

## 5. Making a change

```csharp
var launcher = new ElevatedLauncher(env, exePath);   // the exe's file path (section 4)
var progress = new Progress<LaunchStage>(stage => /* "Waiting for your permission..." / "Changing the hosts file..." / taking long */);
IntPtr owner = originForm.Handle;   // the window whose button started the change; read it on the UI thread
LaunchResult r = await launcher.RunAsync(ElevatedCommand.On(Level.Strict), progress, owner);   // worker thread
// then: re-read the status, show the result
```

Stages, in this order: `WaitingForPermission` (at once), `Changing` (the elevated run started, after the Windows
prompt), then `TakingLong` once if that run is still going `ElevatedLauncher.TakingLongAfter` (30 s) later. A normal
run takes a few seconds; only many retries on a busy file plus a slow DNS flush, or something holding the run (such
as security software), get it to 30 s. After `TakingLong` the launcher keeps waiting: it never stops
or abandons the run, and the result arrives as usual whenever the run exits. No stage is reported after `RunAsync`
completes, so a late `TakingLong` can't overwrite a result. A long wait for the Windows prompt is not reported (the
user may be reading it); the window can time that itself from `WaitingForPermission` if it wants a hint.

`owner` (optional, default `IntPtr.Zero`): in the real environment the launcher passes it to Windows as the prompt's
owner (`ProcessStartInfo.ErrorDialog` and `ErrorDialogParentHandle`, Microsoft's documented pattern), so the prompt
comes up in front of that window instead of possibly only flashing on the taskbar. Declining is still Declined
(Win32Exception 1223); if the start fails otherwise, Windows may show its own message first. With `IntPtr.Zero`
nothing changes. The sandbox ignores it (no prompt). The beta confirms the real prompt's behavior.

Commands: `ElevatedCommand.On(level)`, `Off()`, `Allow(hosts)`, `Block(hosts)` (1 to
`Limits.MaxHostsPerRun` = 100 hosts; more throws), `Undo()`. The real environment starts the exe
with `runas`, hidden, shell execute; the sandbox starts it without `runas` with `--sandbox`.

The command line (built by `FormatArguments`; never build it by hand): `--elevated on --level standard|strict --end`,
`--elevated off --end`, `--elevated undo --end`, `--elevated allow --count N host... --end` and
`--elevated block --count N host... --end`; Debug sandbox runs put `--sandbox` first. Every command ends with
`--end`, and N must equal the number of hosts given, so a command line cut short on its way through the
Windows prompt exits 15 instead of running as a shorter command.

| `r.Kind` | Exit code | Window |
|---|---|---|
| `Success` | 0 | a result bar: success (Turn on after the self-check passed, level changed, newer list, Allow, Block again, Repair, older list removed), info (turned off, Undo), or warning (Turn on whose self-check failed, with "Copy details") |
| `Declined` | none (Win32Exception 1223) | an info result bar, "Nothing was changed." |
| `AccessDenied` | 10 | error dialog (Try again) |
| `FileLocked` | 11 | error dialog (Try again) |
| `Reverted` | 12 | error dialog (Try again) |
| `Busy` | 13 | an info result bar (QuietHosts was already changing the hosts file) |
| `Refused` | 16 | re-read the status: if it is `Refused`, no bar (the status card explains why; screen readers hear "Nothing was changed." with its headline and detail); if not (the file is fine but the change would make it larger than 16 MB), a warning result bar |
| `Unexpected` | 15, 14, any other code, or a start failure (`r.Error`) | error dialog in two forms: `r.HostsFileChanged == false` means "unchanged" (Try again); `true` or `null` means "may be incomplete" (Repair, which runs the same command again) |

`ExitCodes.ToResult(code)` / `FromResult(kind)` do the mapping. "Try again" reruns the same command.

What each verb does (the elevated run re-derives everything itself):
- `on --level L`: writes the block for L, sets Enabled=1 and Level=L.
- `off`: removes both blocks, Enabled=0; Level and Allowlist stay.
- `allow` / `block`: change the allowlist. They follow the file, as the window does: if our block is in the file, they rewrite it at the block's own level (from its header) and save Enabled=1 and that level; if the block is missing but Enabled=1, they rewrite it at the saved level; otherwise only the allowlist changes. Hosts must be in the lists (or, for block, in the allowlist); otherwise exit 15.
- A FileLocked or AccessDenied result means the file is as it was: if a failed write had changed it, it was rolled back first (and if that failed, the result is Unexpected).
- After a write that leaves the file neither as it was nor as intended (or unreadable), the pending marker stays, so `ChangeNotFinished` shows until the next elevated run's recovery.
- `undo`: removes both blocks and deletes `HKLM\SOFTWARE\QuietHosts`. Backups stay.
- Every write moves hand-disabled `#0.0.0.0 host` lines from the block into the allowlist and prunes allowlist entries that left the lists.

## 6. Self-check (after a successful Turn on)

```csharp
SelfCheckResult c = await SelfCheck.CheckAsync(env, status.FirstBlockedHost);  // DnsQuery_W cache-only; sandbox reads its file
// c.Passed: TurnOnOk; otherwise SelfCheckFailed. c.Detail goes to Copy details.
```

After SelfCheckFailed the main window's result bar shows a "Copy details" link; its text includes a "Self-check" line with
`c.Detail`, so a report can tell a slow DNS cache from a proxy or VPN that bypasses the hosts file.

## 7. Fix dialog

```csharp
List<FixRow> rows = FixList.Build(lists, status.SelectedLevel, status.Allowlist);  // allowed first, then A-Z
List<FixRow> shown = FixList.Search(rows, query);  // address, company, "What it is", note; case-insensitive
```

`FixRow`: `Host`, `Company`, `CategoryWords`, `Tier` (window words Default = "Standard",
Strict = "Strict"), `Note` ("" = "None known"), `Allowed`.
The Status column comes from `FixRow.Allowed` and `HostsStatus.BlockedHosts` (the entries of QuietHosts'
block in the file): "Allowed by you"; "Blocked" (in the block); "Not blocked"; or "Unknown" when the file
can't be read or changed (Can't read, Refused) or the older block-ads.ps1 list is in it.
Disable Allow / Block again above `Limits.MaxHostsPerRun` selected rows.

## 8. Per-user settings (the window writes these itself)

`env.User` (`IUserStore`): `Read()` gives `FirstRunDone`, `PendingLevel`, `MainX`, `MainY`,
`FixW`, `FixH`, `DismissedNotices`; setters `SetFirstRunDone`, `SetPendingLevel(level or null)`,
`SetMainPosition`, `SetFixSize`, `SetDismissedNotices(entries)`; `DeleteAll()` after a successful Undo.
Level change while off: `SetPendingLevel(level)`, no prompt. Turn on uses `status.SelectedLevel`.
`env.Machine` is read-only for the window (only elevated runs write it).

Closed notices: `DismissedNotices` holds entries `"<id>=<fingerprint>"` (for example `edited-block=3fa0...` or
`large-file=size:1480000`). `NoticeDismissalText.Clean(raw)` keeps the entries that match
`\A[a-z][a-z0-9-]{0,31}=[A-Za-z0-9.:|_-]{1,95}\z` and are at most `NoticeDismissalText.MaxLength` (128) characters,
keeps the last entry per id (in the place of the first), and at most the first `NoticeDismissalText.MaxEntries` (16);
ordinal and culture-free. It doesn't know the ids: the window drops ids it doesn't know. `Read()` returns the cleaned
list; `SetDismissedNotices` stores `Clean(entries)` and deletes the value when that is empty, leaving every other
setting as it is (and every other setter leaves the dismissals as they are). Where it is stored:

| Store | Where |
|---|---|
| `RegistryUserStore` | `HKCU\Software\QuietHosts`, value `DismissedNotices` (`REG_MULTI_SZ`; any other type reads as nothing dismissed); `DeleteAll` removes it with the key |
| `FileUserStore` (Debug sandbox) | `user.ini`, key `DismissedNotices`: the entries joined with "\n" |
| `InMemoryUserStore` | in memory |

Only the window writes it, unelevated. Elevated runs never read or write it.

## 9. Copy details

`status.LastRun` (time, app version, command with a host count, result, counts, backup path,
log lines, exception; never file contents), `AppInfo.Version`, `lists.Version`,
`Diagnostics.WindowsBuild()`, the `ResultKind`, `ElevatedCommand.Describe()`, `r.Error`, and a "Text size" line
(Windows' text-size setting in percent and the message font's size in points, read-only). The Fix dialog's "Copy details"
is a shorter site report: app, block lists, Windows, state, level, the number of allowed addresses (never their names)
and the text size. Pass the whole text through `Diagnostics.MaskProfilePath(text)` so the profile path shows as
`%USERPROFILE%`.

## 10. Enums

| Enum | Values |
|---|---|
| `Level` | `Standard` (tier Default), `Strict` (Default and Strict) |
| `Tier` | `Default` (lists/default.txt), `Strict` (lists/strict.txt) |
| `ProtectionState` | `Off`, `On`, `Removed`, `OlderList`, `CantRead`, `Refused` (section 4) |
| `RefusalReason` | `None`, `HostsPathMoved`, `UnsupportedEncoding`, `TooLarge` |
| `HostsWarnings` (flags) | `None`, `EditedBlock` 1, `MalformedMarkers` 2, `LargeFile` 4, `MicrosoftLinesOutside` 8, `ChangeNotFinished` 16, `LegacyBlockToo` 32 |
| `ResultKind` | `Success`, `Declined`, `AccessDenied`, `FileLocked`, `Reverted`, `Busy`, `Refused`, `Unexpected` |
| `Verb` | `On`, `Off`, `Allow`, `Block`, `Undo` |
| `LaunchStage` | `WaitingForPermission`, `Changing`, `TakingLong` (section 5) |
| `RunLocationTips` (flags) | `None`, `RunningFromDownloads`, `RunningFromTemp` |

A test pins the names and numbers of the first seven; the window should word every value.

## 11. What the window does itself

- Single instance (`Local\QuietHosts.Window`, bring the first window forward); the elevated run is exempt.
- Never run the elevated path on the UI thread: use `RunAsync`, `CheckAsync`, and read status off the UI thread if a locked file could stall it.
- Keep action buttons disabled while a change runs; re-read the status after every result.
- Hash comparison for Unexpected is done by the launcher (`r.HostsFileChanged`).
- Links: when the window itself runs elevated (`ElevatedMain.IsProcessElevated()`), open them with `explorer.exe "<url>"`; otherwise a normal shell open. URLs: `AppInfo.RepositoryUrl` + `/releases`, `/issues/new?template=site-broke.yml`, `/issues/new?template=bug.yml`, and the README anchors `#if-something-goes-wrong`, `#verify-your-download`, `#quiethosts-wont-change-your-hosts-file` and `#windows-defender-reports-hostsfilehijack`. A link or folder that doesn't open shows QuietHosts' own dialog with the address to copy.
- All strings, number grouping ("1,035") and date formatting (InvariantCulture) are in the window.
- `--render-ui`: build fake `HostsStatus` objects with object initializers (all properties are `init`); `LaunchResult` and `SelfCheckResult` have public constructors.
