# Update QuietHosts

QuietHosts never updates itself and never downloads anything. New versions, with new block lists, come out on the [releases page](https://github.com/quiethosts/QuietHosts/releases). Updating takes a minute and at most one Windows prompt.

[User guide](README.md) › Update QuietHosts

## Check for a new version

In QuietHosts, click **Check for a newer version**, under the level cards (it's also in **About**). It opens the releases page in your browser. The main window's "Block list from …" line and **About** show which version and list you have.

To hear about new versions, you can watch the repository on GitHub: **Watch > Custom > Releases**. That needs a GitHub account.

## Install the new version

1. Download QuietHosts.exe from the newest release and [check it](getting-started.md#check-your-download).
2. Close QuietHosts.
3. Replace the old QuietHosts.exe in its folder with the new one.
4. Start QuietHosts.

Your settings stay: protection, your level and your allowed addresses are stored for the whole PC, not in QuietHosts.exe.

## Use the newer list

Your hosts file keeps the old list until QuietHosts writes it again. If protection is on, the new version says "This copy of QuietHosts has a newer block list (…)."

1. Click **Use newer list** in that notice.
2. Say yes to the Windows prompt if it names QuietHosts.exe.
3. QuietHosts says "Updated to the block list from 20 Nov 2026. Restart your browser to see the change." (with the new list's date).
4. Restart your browser.

Protection stays on the whole time, and your level and allowed addresses are kept. Any other change, such as switching the level, also writes the newer list.

If protection is off, there is nothing to do: QuietHosts uses its newer list the next time you turn protection on.

## If QuietHosts says your hosts file has a newer list

If QuietHosts says "Your hosts file has a newer block list (…). Changes made with this copy put an older list back.", you started an older QuietHosts.exe than the one that wrote your hosts file. Use the newest one: **Get the newest QuietHosts** in that notice opens the releases page.

## Test versions

Versions such as 1.0.0-beta.1 are test versions. They are marked **Pre-release** on the releases page, and the [latest release](https://github.com/quiethosts/QuietHosts/releases/latest) link never points to them. Use them only if you want to help test, and please [report](troubleshooting.md#report-a-problem) anything that doesn't work.
