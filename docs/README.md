# QuietHosts user guide

QuietHosts blocks ad and tracker addresses for every browser and app on your Windows PC, using the Windows hosts file. This guide shows you how to set it up, what each part of the window does, and what to do when something doesn't work. You don't need to know anything about the hosts file to use it.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/main-on-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="images/main-on-light.png">
  <img src="images/main-on-light.png" alt="The QuietHosts main window with protection on. The status card says Protection is on, how many ad and tracker addresses QuietHosts blocks with Standard, and that it stays on after you close the window or restart, with a Turn off protection button. Below it, the Blocking level section shows the Standard card, tagged Recommended and marked In use, and the Strict card with a Use Strict… button and a What Strict changes link. At the bottom are the date of the block list, a Check for a newer version link, and the buttons Fix a site or app…, Undo all changes… and About.">
</picture>

## Contents

1. [Getting started](getting-started.md): download QuietHosts, check the file, get past the Windows warnings and turn protection on.
2. [Blocking levels](blocking-levels.md): Standard and Strict, and what Strict can break.
3. [Fix a site or app](fix-a-site.md): what to do when a site or app stops working, and how to allow or block single addresses.
4. [Notices and messages](notices.md): every message QuietHosts shows, what it means and what to do.
5. [Troubleshooting](troubleshooting.md): antivirus prompts, Windows Defender, a hosts file QuietHosts can't read or change, backups and more.
6. [Update QuietHosts](update.md): new versions and new block lists.
7. [How it works](how-it-works.md): the hosts file, the permission prompts, backups, what QuietHosts never blocks and what it can't block.
8. [Uninstall](uninstall.md): take QuietHosts' lines and settings away, then delete the file.
9. [FAQ](faq.md): short answers to common questions.

## The main window

| Part | What it does |
|---|---|
| The status card, at the top | Says whether protection is on, and with which level. Its button names what it does: **Turn on protection** or **Turn off protection**. If something is wrong, the card says what and how to fix it. |
| Notices, under the status card | Short messages: the result of your last change, warnings and tips. See [Notices and messages](notices.md). |
| **Blocking level** | One card for Standard and one for Strict. See [Blocking levels](blocking-levels.md). |
| **Block list from …** | The date of the block list in use: while protection is on, the one in your hosts file; otherwise the one built into your copy. **Check for a newer version** opens the releases page in your browser. |
| **Fix a site or app…** | Helps you when a site or app stops working. See [Fix a site or app](fix-a-site.md). |
| **Undo all changes…** | Takes QuietHosts' lines out of the hosts file and deletes its settings; the backups stay. See [Uninstall](uninstall.md). |
| **About** | The version, the block list, privacy, your backups, the signature, the license and help links. |

A button with the Windows shield changes the hosts file, so Windows asks for your permission when you click it. Buttons without the shield never do. Links with a small arrow open in your browser.

## The About window

| Section | What you find there |
|---|---|
| **Block list** | The date of your block list and how many addresses Standard and Strict block. **Check for a newer version**. |
| **Privacy** | QuietHosts never goes online and collects nothing. |
| **Your hosts file** | **Open backup folder** opens the folder with the hosts file and its backups. **How QuietHosts works** opens a short explanation inside QuietHosts. |
| **Signature** | Whether your copy is digitally signed. **How to check your download** opens the steps in your browser. |
| **License and credits** | **View license** shows the MIT License. |
| **Help** | **Project page**, **Report a problem** and **If something goes wrong** open in your browser. |
| **Uninstall** | "Click Undo all changes, then delete QuietHosts.exe." |

## Get help

- Something doesn't work? Start with [Troubleshooting](troubleshooting.md) and the [FAQ](faq.md).
- A site or app broke? Use [Fix a site or app](fix-a-site.md), then the [broken-site form](https://github.com/quiethosts/QuietHosts/issues/new?template=site-broke.yml).
- Found a bug? Use the [problem form](https://github.com/quiethosts/QuietHosts/issues/new?template=bug.yml).
- Found a security problem? Please report it privately, as [SECURITY.md](../SECURITY.md) describes.

[SUPPORT.md](../SUPPORT.md) lists every way to get help. Back to the [project page](../README.md).
