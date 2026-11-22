# Changelog

All notable changes to QuietHosts are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The first version of QuietHosts. QuietHosts.exe isn't signed yet, so Windows may warn about it; the README explains what to do.

### Added

- Blocks ad and tracker addresses for every browser and app on the PC, using the Windows hosts file. Nothing runs in the background, and blocking keeps working after a restart.
- Two levels. Standard is picked so that sites, sign-ins, payments and games keep working. Strict blocks more, for people who don't mind fixing a site now and then: some links, videos, site features and apps may stop working, and it says so before you switch. You can switch back to Standard at any time.
- "Fix a site or app…" walks you through fixing a site, step by step: restart your browser, try Standard, test with protection off. It lets you search for an address, see what blocking it may break, allow it, and block it again later.
- Undo all changes removes QuietHosts' lines from the hosts file and deletes its settings. Lines added by you or other programs stay, and so do the backups.
- A backup of the hosts file before every change, in the same folder: the file as it was before the first change, plus the 10 newest copies.
- Every change is checked. QuietHosts reads the file back, checks it again 2 seconds later, and puts the old file back if needed.
- Clear messages when an antivirus blocks or undoes a change, when the hosts file is in use, when a change takes longer than usual or is still running, or when the file can't be changed safely.
- Tells you when QuietHosts' lines were removed or changed outside the app, and when other lines may make Windows Defender reset the hosts file, in short notices you can close; a closed notice comes back only when its cause changes.
- Follows Windows' dark mode on Windows 11, and high-contrast themes on both Windows 10 and 11.
- Never blocks Microsoft addresses, or the services that sign-in, payments, captchas and security updates need.
- Works offline. QuietHosts never goes online and has no telemetry. The block list is built in, and new lists come with new versions.
- One file with nothing to install, for Windows 10 (version 1903 or later) and Windows 11.
- Can be used with the keyboard alone. It is built to work with screen readers and high-contrast themes, but these haven't been tested yet; please report anything that doesn't work.
- A [user guide](https://github.com/quiethosts/QuietHosts/blob/main/docs/README.md) that explains every screen and message, with the steps for common problems.

## [1.0.0-beta.1] - 2026-11-22

A test version of the first QuietHosts release. Please [report anything that doesn't work](https://github.com/quiethosts/QuietHosts/issues/new/choose). QuietHosts.exe isn't signed yet, so Windows may warn about it; the README explains what to do.

### Added

- Blocks ad and tracker addresses for every browser and app on the PC, using the Windows hosts file. Nothing runs in the background, and blocking keeps working after a restart.
- Two levels. Standard is picked so that sites, sign-ins, payments and games keep working. Strict blocks more, for people who don't mind fixing a site now and then: some links, videos, site features and apps may stop working, and it says so before you switch. You can switch back to Standard at any time.
- "Fix a site or app…" walks you through fixing a site, step by step: restart your browser, try Standard, test with protection off. It lets you search for an address, see what blocking it may break, allow it, and block it again later.
- Undo all changes removes QuietHosts' lines from the hosts file and deletes its settings. Lines added by you or other programs stay, and so do the backups.
- A backup of the hosts file before every change, in the same folder: the file as it was before the first change, plus the 10 newest copies.
- Every change is checked. QuietHosts reads the file back, checks it again 2 seconds later, and puts the old file back if needed.
- Clear messages when an antivirus blocks or undoes a change, when the hosts file is in use, when a change takes longer than usual or is still running, or when the file can't be changed safely.
- Tells you when QuietHosts' lines were removed or changed outside the app, and when other lines may make Windows Defender reset the hosts file, in short notices you can close; a closed notice comes back only when its cause changes.
- Follows Windows' dark mode on Windows 11, and high-contrast themes on both Windows 10 and 11.
- Never blocks Microsoft addresses, or the services that sign-in, payments, captchas and security updates need.
- Works offline. QuietHosts never goes online and has no telemetry. The block list is built in, and new lists come with new versions.
- One file with nothing to install, for Windows 10 (version 1903 or later) and Windows 11.
- Can be used with the keyboard alone. It is built to work with screen readers and high-contrast themes, but these haven't been tested yet; please report anything that doesn't work.
- A [user guide](https://github.com/quiethosts/QuietHosts/blob/main/docs/README.md) that explains every screen and message, with the steps for common problems.

[Unreleased]: https://github.com/quiethosts/QuietHosts/commits/main
[1.0.0-beta.1]: https://github.com/quiethosts/QuietHosts/releases/tag/v1.0.0-beta.1
