# Frequently asked questions

[User guide](README.md) › FAQ

- **About QuietHosts:** [What does it do?](#what-does-quiethosts-do) · [Is it free?](#is-quiethosts-free) · [Does it collect data?](#does-quiethosts-collect-any-data) · [Is it safe?](#is-quiethosts-safe-to-use) · ["Unknown publisher"](#why-does-windows-say-unknown-publisher)
- **Blocking:** [All ads?](#does-it-block-all-ads) · [Browser extensions](#do-i-still-need-an-ad-blocking-browser-extension) · [Browsers and apps](#does-it-work-in-every-browser-and-app) · [VPN](#does-it-work-with-a-vpn) · [Which addresses](#which-addresses-does-it-block) · [Your own addresses](#can-i-add-my-own-addresses) · [Malware](#does-it-protect-me-from-malware-or-phishing)
- **Everyday use:** [Keep it open?](#do-i-need-to-keep-quiethosts-open) · [Permission prompts](#why-does-windows-ask-for-permission-every-time) · [Other users](#does-it-work-for-every-user-on-the-pc) · [Speed](#does-it-slow-down-my-pc-or-my-internet) · [Other hosts lines and tools](#does-it-work-with-my-own-hosts-file-lines-or-other-tools) · [Work PCs](#can-i-use-it-on-a-work-or-school-pc) · [Dark mode](#does-quiethosts-have-a-dark-mode) · [Keyboard and screen readers](#can-i-use-quiethosts-with-the-keyboard-or-a-screen-reader) · [Windows versions](#which-windows-versions-does-it-run-on)
- **Updates and removal:** [New versions](#how-do-i-get-new-versions-and-new-lists) · [Deleting the exe](#what-happens-if-i-just-delete-quiethostsexe) · [Helping](#how-can-i-help)

## About QuietHosts

### What does QuietHosts do?

It blocks ad and tracker addresses for every browser and app on your Windows PC. It adds them to the Windows hosts file and points them nowhere (0.0.0.0), so nothing on the PC can load them. [How it works](how-it-works.md) explains the details.

### Is QuietHosts free?

Yes. It's free and open source under the [MIT License](../LICENSE). It has no ads, no paid version and no account. The QuietHosts contributors make it.

### Does QuietHosts collect any data?

No. QuietHosts never goes online. It has no telemetry or analytics, and it never checks for updates by itself. Links in QuietHosts open in your web browser. **Copy details** copies text to your clipboard; nothing is sent unless you paste it into a report yourself.

### Is QuietHosts safe to use?

QuietHosts is built to change as little as possible, and to tell you exactly what it does:

- The window runs without administrator rights. Each change is one hidden administrator run that makes that change and closes, after Windows asks for your permission.
- Before every change it saves a backup of the hosts file, and afterwards it checks the result. It changes only its own block; your own lines stay.
- It never blocks Microsoft addresses or the services that sign-in, payments, captchas and security updates need.
- Its source code is public, and every release is built by GitHub Actions from that code. You can [check your download](getting-started.md#check-your-download).

It has limits too. Like any program you run as administrator, it trusts the folder it's in, so keep it in a folder of its own ([why](getting-started.md#put-quiethosts-in-a-folder-of-its-own)). [SECURITY.md](../SECURITY.md) lists what is and isn't covered, and how to report a security problem privately.

### Why does Windows say "Unknown publisher"?

QuietHosts isn't digitally signed yet. Windows shows "Unknown publisher" for programs without a signature, and SmartScreen may warn about new versions that have no reputation yet. Signed builds are planned. Until then, [check your download](getting-started.md#check-your-download), and say yes to the Windows prompt only if it names QuietHosts.exe and you just clicked a QuietHosts button.

## Blocking

### Does it block all ads?

No. QuietHosts blocks ad and tracker addresses, and some ads come from the same servers as the page itself: video ads on YouTube and Twitch, and sponsored posts on social sites. Blocking those servers would break the sites. A hosts file also can't hide empty ad boxes. See [What it can't block](how-it-works.md#what-it-cant-block).

### Do I still need an ad-blocking browser extension?

For the ads QuietHosts can't block, yes. An extension such as uBlock Origin Lite (Chrome, Edge) or uBlock Origin (Firefox) works well alongside QuietHosts. QuietHosts adds blocking for every other browser and app on the PC.

### Does it work in every browser and app?

It works in browsers and apps that ask Windows to look up addresses, which is most of them. It has no effect when a browser or app uses a proxy, or in apps that look up addresses themselves instead of asking Windows.

### Does it work with a VPN?

Usually, yes: Windows reads the hosts file before it asks any DNS server, including a VPN's. If your VPN or security app sends your browser's traffic through its own proxy, the hosts file has no effect on that traffic.

### Which addresses does it block?

**Fix a site or app…** lists every address your level blocks, with its company, what it is and its known effects. The lists are also public: [lists/default.txt](../lists/default.txt) (Standard) and [lists/strict.txt](../lists/strict.txt) (the extra addresses of Strict). Each address was reviewed one by one.

### Can I add my own addresses?

Not in QuietHosts: it blocks only the addresses on its lists, and you can allow some of them. You can [suggest an address](../CONTRIBUTING.md#the-block-lists) for everyone. You can also add your own `0.0.0.0` lines to the hosts file yourself, outside the QuietHosts block: QuietHosts leaves them as they are (see [Open the hosts file as administrator](troubleshooting.md#open-the-hosts-file-as-administrator)). Don't add Microsoft addresses: Windows Defender may then reset the whole file (see [Windows Defender reports HostsFileHijack](troubleshooting.md#windows-defender-reports-hostsfilehijack)).

### Does it protect me from malware or phishing?

No. QuietHosts blocks ad and tracker addresses. It isn't an antivirus and isn't made to block malware or phishing sites. Keep your antivirus on.

## Everyday use

### Do I need to keep QuietHosts open?

No. Windows itself reads the hosts file, so blocking keeps working after you close QuietHosts, and after a restart. QuietHosts doesn't start with Windows and has nothing running in the background. Open it only when you want to change something.

### Why does Windows ask for permission every time?

The hosts file is a protected Windows file, so Windows asks before every change. QuietHosts doesn't keep administrator rights between changes: each change gets its own permission. Buttons that need this show the Windows shield. See [One permission prompt per change](how-it-works.md#one-permission-prompt-per-change).

### Does it work for every user on the PC?

Yes. The hosts file applies to the whole PC, so protection, the level and the allowed addresses are the same for every Windows user. Each user has only their own window settings, and sees the first screen once, if protection is still off then.

### Does it slow down my PC or my internet?

Not noticeably. QuietHosts doesn't run in the background, and its block in the hosts file is small. Very large hosts files can slow Windows down; QuietHosts tells you if other programs have made yours large.

### Does it work with my own hosts file lines or other tools?

Yes. QuietHosts changes only its own block, and lines added by you or other programs stay exactly as they are. If you already point an address somewhere yourself, QuietHosts leaves it out of its block. A tool that rewrites the whole hosts file may remove QuietHosts' lines; QuietHosts then says "Protection was removed" (see [Protection was removed](troubleshooting.md#protection-was-removed)).

### Can I use it on a work or school PC?

Only if you're allowed to: every change needs an administrator's permission. If an organization manages your PC, ask its IT department first.

### Does QuietHosts have a dark mode?

On Windows 11, QuietHosts follows Windows' app mode (Settings > Personalization > Colors). On Windows 10 it stays light. When a contrast theme is on, QuietHosts uses its colors. It follows a change while it's open.

### Can I use QuietHosts with the keyboard or a screen reader?

QuietHosts can be used with the keyboard alone:

- Tab moves through the window in reading order. Press Alt to see the underlined letters; Alt and a letter clicks that button or link.
- F5 reads the hosts file again. Esc closes the other windows, and means **Not now** on the first screen.
- PageUp, PageDown, Ctrl+Home and Ctrl+End scroll a window whose content doesn't fit.
- In the main window and in **Fix a site or app…**, Enter never turns protection on or off and never changes the level, so a stray key press there can't start a change. Use Space, the underlined letter or a click. In a window that asks a question, such as "Use Strict blocking?", Enter chooses the highlighted button.

QuietHosts is built to work with screen readers such as Narrator, with contrast themes and with Windows' text size setting (a new text size applies the next time you start QuietHosts). These haven't been tested by real users yet; please [report](troubleshooting.md#report-a-problem) anything that doesn't work.

### Which Windows versions does it run on?

Windows 10 (version 1903 or later) and Windows 11. It uses .NET Framework 4.8, which is part of these versions.

## Updates and removal

### How do I get new versions and new lists?

QuietHosts never updates itself. Click **Check for a newer version** to open the releases page. New lists come with new versions. See [Update QuietHosts](update.md).

### What happens if I just delete QuietHosts.exe?

Blocking stays, because the lines stay in the hosts file. To remove them, start QuietHosts and click **Undo all changes…** first, or remove them by hand: see [Uninstall](uninstall.md).

### How can I help?

- Report a site that QuietHosts breaks with the [broken-site form](https://github.com/quiethosts/QuietHosts/issues/new?template=site-broke.yml). These reports keep Standard safe for everyone.
- Suggest, move or remove an address: [CONTRIBUTING.md](../CONTRIBUTING.md) explains how.
- Report a problem with the [problem form](https://github.com/quiethosts/QuietHosts/issues/new?template=bug.yml), or a security problem privately, as [SECURITY.md](../SECURITY.md) describes.
