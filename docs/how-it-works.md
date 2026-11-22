# How QuietHosts works

QuietHosts does one thing: it keeps a block of lines in the Windows hosts file. This page explains that file, what QuietHosts writes into it, how each change is made safely, and where the limits are.

[User guide](README.md) › How it works

## The hosts file

Every Windows PC has a text file named `hosts`, in `C:\Windows\System32\drivers\etc`. Windows reads it before it looks up an address on the internet.

A line such as `0.0.0.0 pagead2.googlesyndication.com` sends that address nowhere. 0.0.0.0 isn't a real place, so no browser or app on this PC can load anything from it. That works for every browser and app that asks Windows to look up addresses, and for every user account on the PC.

## The QuietHosts block

QuietHosts adds one block of lines to the hosts file. It looks like this:

```
# >>> QuietHosts >>>
# Managed by QuietHosts: use the app to change these lines. https://github.com/quiethosts/QuietHosts
# QuietHosts <version> | level=standard | lists=<date> | hosts=<count> | sha256=<checksum>
0.0.0.0 1x1.a-mo.net
0.0.0.0 …
# <<< QuietHosts <<<
```

- The first and the last line mark where the block starts and ends.
- The "Managed by" line says that these lines belong to QuietHosts, with a link to this project.
- The next line holds the QuietHosts version that wrote the block, the level, the date of the block list, how many addresses it blocks and a checksum. The checksum lets QuietHosts notice when another program changes its lines.
- Then comes one line for each blocked address, from A to Z.

**Your own lines stay.** QuietHosts changes only its own block. Lines that you or other programs added stay exactly as they are. If you already point an address somewhere yourself, QuietHosts leaves it out of its block.

**Use the app to change the block.** If you put `#` in front of one of its address lines, QuietHosts treats that address as allowed at your next change. A line of your own that you add inside the block is moved, unchanged, to just below it at your next change. Anything else you change in the block is written back.

## One permission prompt per change

- The QuietHosts window runs without administrator rights. It reads the hosts file, which every program may read, but never writes it.
- For each change, it starts a hidden copy of itself with administrator rights, so Windows asks for your permission first. That copy makes the one change, records the result and closes. It trusts nothing from the window: it works out what to write from the block list built into it, its settings and the change it was asked for.
- That's why Windows asks before every change, and why the buttons that change the hosts file show the Windows shield. Buttons that only read the file, such as **Check again** on the status card, never ask.
- Only one change runs at a time. While another QuietHosts change runs, the buttons that change the hosts file wait until it ends.

Say yes only if the prompt names QuietHosts.exe and you just clicked a QuietHosts button. If you say no, nothing is changed.

## Backups and checks

For every change to the file, QuietHosts:

1. **Saves a backup.** It writes a copy of the hosts file into the same folder and reads the copy back to check it. Without a good backup, it doesn't change anything.
2. **Writes the new file and checks it.** It reads the file back right away and again 2 seconds later. If the change didn't stick, it tells you, and it puts the old file back if needed.
3. **Clears Windows' address cache** (`ipconfig /flushdns`), so Windows forgets old answers.

After you turn protection on, QuietHosts also asks Windows' address cache for one of the blocked addresses, to check that Windows uses the list. This check never goes online. If it can't confirm it yet, QuietHosts says so (see [Ads still show](troubleshooting.md#ads-still-show)).

The backups:

- `hosts.quiethosts-original.bak` is your hosts file as it was before QuietHosts changed it the first time. QuietHosts never overwrites or deletes it.
- `hosts.quiethosts-<date>-<time>.bak` is a copy from just before a change. QuietHosts keeps the 10 newest. The date and time are in UTC.

[Restore a backup](troubleshooting.md#restore-a-backup) shows how to put one back.

QuietHosts doesn't change a hosts file that isn't safe to change: one that Windows reads from another folder, one saved as UTF-16 or another format it can't write safely, or one larger than 16 MB. It tells you why instead (see [QuietHosts won't change your hosts file](troubleshooting.md#quiethosts-wont-change-your-hosts-file)).

## Restart your browser after a change

Browsers remember addresses for a few minutes. Close all browser windows and open the browser again to see a change. QuietHosts clears the Windows address cache itself.

## Nothing runs in the background

QuietHosts has no service, no startup entry and no scheduled task. When you close its window, it isn't running at all. Windows itself reads the hosts file, so blocking keeps working after you close QuietHosts, and after a restart.

The window shows the state of the hosts file when it opens, after each change, and when you press F5.

## What QuietHosts never blocks

- **Microsoft addresses.** Windows Defender resets hosts files that block them (see [Windows Defender reports HostsFileHijack](troubleshooting.md#windows-defender-reports-hostsfilehijack)), so QuietHosts never does. A list built into the app protects them.
- **Services that sign-in, payments, captchas and security updates need.** [lists/guard.txt](../lists/guard.txt) protects them, together with widely used services that everyday use depends on.
- **Names for local networks**, such as `localhost`.
- **Addresses you allowed**, and addresses you already point somewhere yourself.

These rules are built into QuietHosts: even a block list that contained such an address couldn't make QuietHosts write it.

## What QuietHosts never does

- **It never goes online.** It has no telemetry, analytics, ads or accounts, and it never checks for updates by itself. Links such as **Check for a newer version** and **Report a problem** open in your web browser.
- **It changes nothing else.** It changes only the hosts file (keeping a backup next to it) and its own settings in the registry, and it clears Windows' address cache after each change.
- **It never changes your antivirus or Windows settings,** and never asks you to turn any protection off.

## What it can't block

YouTube video ads, Twitch stream ads and sponsored posts on social sites come from the same servers as the videos and posts, so blocking them would break those sites. For those, use an ad-blocking browser extension such as uBlock Origin Lite (Chrome, Edge) or uBlock Origin (Firefox). It works well alongside QuietHosts.

Also:

- **Exact addresses only.** A hosts file can't block "everything under example.com". Each address must be on the list, so trackers on a site's own subdomains usually get through.
- **No hiding.** QuietHosts can't hide empty ad boxes or other parts of a page. A browser extension can.
- **Not through a proxy.** It has no effect when a browser or app uses a proxy, or in apps that look up addresses themselves instead of asking Windows.
- **Never Microsoft addresses.** Windows Defender resets hosts files that block them, so QuietHosts never does.

## The block list

QuietHosts' block list is our own. Each address was reviewed one by one and checked against public lists by Steven Black, HaGeZi, OISD, AdGuard, Peter Lowe and the EasyList authors. QuietHosts doesn't include or download their lists. [CREDITS.md](../CREDITS.md) has the details.

| File | What it holds |
|---|---|
| [lists/default.txt](../lists/default.txt) | Standard: blocked for everyone who turns QuietHosts on. |
| [lists/strict.txt](../lists/strict.txt) | Strict: blocked only when Strict is chosen. Each address has a note on what may stop working. |
| [lists/guard.txt](../lists/guard.txt) | Addresses that are never blocked. |

The list is built into QuietHosts.exe. New lists come with new versions of QuietHosts; the main window's "Block list from …" line shows the date of yours. See [Update QuietHosts](update.md). To suggest a change to the list, read [CONTRIBUTING.md](../CONTRIBUTING.md).

## Where settings are kept

QuietHosts keeps its settings in the registry. Apart from them and the backups next to the hosts file, it stores nothing on your PC.

**For the whole PC**, in `HKEY_LOCAL_MACHINE\SOFTWARE\QuietHosts` (only the hidden administrator copy writes these):

| Setting | What it holds |
|---|---|
| Enabled, Level | Whether protection is on, and your level. |
| Allowlist | The addresses you allowed. |
| LastRun | A short record of the last change, used by **Copy details**: the time, the version, what was changed, the result and any error message. Never the contents of the hosts file. |
| PendingBefore, PendingAfter | Two checksums of the hosts file, kept only while a change runs, and after one that didn't finish. |

**For each Windows user**, in `HKEY_CURRENT_USER\Software\QuietHosts`: whether you have seen the first screen, a level you picked while protection was off, window positions and sizes, and which notices you closed.

**Undo all changes…** deletes both. [Uninstall](uninstall.md) explains what stays.
