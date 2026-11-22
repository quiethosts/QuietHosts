using System;
using System.Globalization;
using System.Text;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// Every string the window shows, in one place so wording can be fixed or translated
    /// later. "&amp;" marks an access key. Numbers use 1,035-style grouping and dates "d MMM yyyy", both with
    /// InvariantCulture, so the text matches its English wording on every PC, whatever its language settings.
    /// Depends only on Core and the BCL, so the tests compile it too.
    /// </summary>
    internal static class Strings
    {
        // Non-ASCII characters are escaped so the file reads the same in any editor encoding.
        private const string Ellipsis = "\u2026";
        private const string OpenQuote = "\u201C";
        private const string CloseQuote = "\u201D";
        private const string MiddleDot = "\u00B7";

        // ---------------------------------------------------------------- app

        public const string AppName = "QuietHosts";

#if DEBUG
        /// <summary>Debug sandbox only: appended to every window title.</summary>
        public const string SandboxTitleSuffix = " (sandbox)";
#endif

        // ---------------------------------------------------------------- formatting

        /// <summary>1,035-style grouping, whatever the PC's culture.</summary>
        public static string Number(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

        /// <summary>"20 Nov 2026".</summary>
        public static string Date(DateTime date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>"1 address" or "1,035 addresses".</summary>
        public static string Addresses(long count) => Count(count, "address", "addresses");

        /// <summary>"1 ad and tracker address" or "920 ad and tracker addresses".</summary>
        public static string AdAddresses(long count) => Count(count, "ad and tracker address", "ad and tracker addresses");

        /// <summary>The number and the singular or plural noun.</summary>
        public static string Count(long count, string one, string many) => count == 1 ? "1 " + one : Number(count) + " " + many;

        /// <summary>A list version "YYYY-MM-DD" as a date ("20 Nov 2026"), or the text itself if it isn't one.</summary>
        public static string ListDate(string? version)
        {
            if (string.IsNullOrEmpty(version)) return UnknownDate;
            return DateTime.TryParseExact(version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
                ? Date(d)
                : version!;
        }

        /// <summary>
        /// True if the text is a list version: a real date written "YYYY-MM-DD", ASCII digits only. Two such versions
        /// compare by date with an ordinal comparison. The hosts file's header can hold anything someone typed.
        /// </summary>
        public static bool IsListVersion(string? version)
        {
            if (version == null || version.Length != 10 || version[4] != '-' || version[7] != '-') return false;
            for (int i = 0; i < version.Length; i++)
            {
                if (i != 4 && i != 7 && (version[i] < '0' || version[i] > '9')) return false;
            }
            return DateTime.TryParseExact(version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        /// <summary>"312 KB" or "1.5 MB" (1 KB = 1,024 bytes).</summary>
        public static string FileSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const long Kb = 1024, Mb = 1024 * 1024;
            if (bytes < 1000 * Kb) return Number(Math.Max(1, (bytes + Kb / 2) / Kb)) + " KB";
            double mb = (double)bytes / Mb;
            return (mb >= 100 ? mb.ToString("#,0", CultureInfo.InvariantCulture) : mb.ToString("0.0", CultureInfo.InvariantCulture)) + " MB";
        }

        public const string UnknownDate = "an unknown date";

        /// <summary>Straight double quotes in text from Core become typographic ones, like the rest of the window.</summary>
        public static string Typographic(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('"') < 0) return text ?? string.Empty;
            var sb = new StringBuilder(text.Length);
            bool open = true;
            foreach (char c in text)
            {
                if (c == '"') { sb.Append(open ? OpenQuote : CloseQuote); open = !open; }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The text without its access-key marker, for accessible descriptions ("&amp;&amp;" stays one "&amp;").</summary>
        public static string WithoutAccessKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&' && i + 1 < text.Length) { i++; sb.Append(text[i]); }
                else if (text[i] != '&') sb.Append(text[i]);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- words

        public static string LevelName(Level level) => level == Level.Strict ? "Strict" : "Standard";

        /// <summary>The Fix dialog's Level column: tier Default is "Standard".</summary>
        public static string TierName(Tier tier) => tier == Tier.Strict ? "Strict" : "Standard";

        /// <summary>"What it is": the category's UI words. Unknown keys read as "Ads or tracking".</summary>
        public static string CategoryWords(string? key) => Typographic(Categories.Words(key));

        // ---------------------------------------------------------------- shared component texts

        /// <summary>Added to the description of every button with the UAC shield (the shield is invisible to screen readers).</summary>
        public const string ShieldDescription = "Windows asks for your permission.";
        /// <summary>Added to the description of every button or link that opens the browser.</summary>
        public const string OpensInBrowser = "Opens in your browser.";
        public const string CloseNoticeName = "Close notice";
        public const string ClearSearchName = "Clear search";

        /// <summary>The start of a notice bar's accessible name, by severity.</summary>
        public const string PrefixError = "Error: ";
        public const string PrefixWarning = "Warning: ";
        public const string PrefixNote = "Note: ";
        public const string PrefixTip = "Tip: ";
        public const string PrefixDone = "Done: ";

        /// <summary>A notice's severity glyph, for screen readers: the prefix word without the colon ("Warning").</summary>
        public static string NoticeGlyphName(NoticeSeverity severity) => NoticePrefix(severity).TrimEnd(' ', ':');

        public static string NoticePrefix(NoticeSeverity severity)
        {
            switch (severity)
            {
                case NoticeSeverity.Error: return PrefixError;
                case NoticeSeverity.Warning: return PrefixWarning;
                case NoticeSeverity.Tip: return PrefixTip;
                case NoticeSeverity.Success: return PrefixDone;
                default: return PrefixNote;
            }
        }

        // ---------------------------------------------------------------- main window: states

        /// <summary>
        /// The status card's headline. Refused because of a moved hosts path or a file over 16 MB is QuietHosts' own safety
        /// rule ("won't change"); an encoding it can't write is an inability ("can't change"); a refusal without a reason
        /// and any other state is the catch-all (S11).
        /// </summary>
        public static string Headline(ProtectionState state, RefusalReason refusal)
        {
            switch (state)
            {
                case ProtectionState.Off: return "Protection is off";
                case ProtectionState.On: return "Protection is on";
                case ProtectionState.Removed: return "Protection was removed";
                case ProtectionState.OlderList: return "Older block list found";
                case ProtectionState.CantRead: return "Can't read your hosts file";
                case ProtectionState.Refused:
                    switch (refusal)
                    {
                        case RefusalReason.HostsPathMoved:
                        case RefusalReason.TooLarge:
                            return "QuietHosts won't change your hosts file";
                        case RefusalReason.UnsupportedEncoding:
                            return "QuietHosts can't change your hosts file";
                        default:
                            return HeadlineUnknown;
                    }
                default: return HeadlineUnknown;
            }
        }

        private const string HeadlineUnknown = "QuietHosts can't tell what's in your hosts file";

        public const string DetailOff = "Ads and trackers aren't blocked.";
        /// <summary>S2: off after "Turn off for now" in the Fix dialog, in this session only. The only "off" nudge.</summary>
        public const string DetailOffAfterTest = "You turned it off to test a site. Turn it on again when you're done.";
        public const string DetailRemoved = "The QuietHosts list is no longer in your hosts file. Windows Security, an antivirus or another program removed it.";
        public const string DetailCantRead = "Another program may be blocking it. Try again in a minute, or restart your PC.";
        public const string DetailUnsupportedEncoding = "It's saved in a format QuietHosts can't change safely, such as UTF-16. Save it as UTF-8, then select Check again.";
        public const string DetailTooLarge = "It's larger than 16 MB, usually because another program added a very large list.";
        public const string DetailUnknown = "Select Check again. If this stays, restart your PC.";
        public const string UnknownFolder = "unknown";

        /// <summary>On: "Blocking {count} ad and tracker addresses with {level}[ (you allowed {k})]. It stays on after ..."</summary>
        public static string DetailOn(int count, Level level, int allowed)
        {
            string text = "Blocking " + AdAddresses(count) + " with " + LevelName(level);
            if (allowed > 0) text += " (you allowed " + Number(allowed) + ")";
            return text + ". It stays on after you close this window or restart.";
        }

        public static string DetailOlderList(int legacyCount) =>
            "Your hosts file has " + Addresses(legacyCount) + " from block-ads.ps1. Turning on replaces them with the QuietHosts list.";

        public static string DetailHostsPathMoved(string? folder) =>
            "Windows is set to use a hosts file in another folder (" + (string.IsNullOrEmpty(folder) ? UnknownFolder : folder) +
            "). Malware sometimes does this, so check your PC with your antivirus.";

        /// <param name="offForTest">Protection was turned off in this session to test a site (S2).</param>
        public static string Detail(HostsStatus s, bool offForTest = false)
        {
            switch (s.State)
            {
                case ProtectionState.Off: return offForTest ? DetailOffAfterTest : DetailOff;
                case ProtectionState.On: return DetailOn(s.BlockedCount, s.BlockLevel ?? s.SelectedLevel, s.AllowedCount);
                case ProtectionState.Removed: return DetailRemoved;
                case ProtectionState.OlderList: return DetailOlderList(s.LegacyCount);
                case ProtectionState.CantRead: return DetailCantRead;
                case ProtectionState.Refused: return RefusedDetail(s.Refusal, s.MovedFolder);
                default: return DetailUnknown;
            }
        }

        public static string RefusedDetail(RefusalReason refusal, string? movedFolder)
        {
            switch (refusal)
            {
                case RefusalReason.HostsPathMoved: return DetailHostsPathMoved(movedFolder);
                case RefusalReason.UnsupportedEncoding: return DetailUnsupportedEncoding;
                case RefusalReason.TooLarge: return DetailTooLarge;
                default: return DetailUnknown;
            }
        }

        /// <summary>S10's link above the main button: the README's steps for saving the hosts file as UTF-8.</summary>
        public const string HowToSaveAsUtf8Link = "&How to do this";

        public const string MainButtonTurnOn = "&Turn on protection";
        public const string MainButtonTurnOff = "&Turn off protection";
        public const string MainButtonTurnOnAgain = "&Turn on again";
        /// <summary>Can't read: reads the file again (no shield).</summary>
        public const string MainButtonTryAgain = "&Try again";
        /// <summary>Refused and the catch-all: reads the file again (no shield). K, so T stays the main button's key elsewhere.</summary>
        public const string MainButtonCheckAgain = "Chec&k again";

        public static string MainButton(ProtectionState state)
        {
            switch (state)
            {
                case ProtectionState.Off:
                case ProtectionState.OlderList:
                    return MainButtonTurnOn;
                case ProtectionState.On: return MainButtonTurnOff;
                case ProtectionState.Removed: return MainButtonTurnOnAgain;
                case ProtectionState.CantRead: return MainButtonTryAgain;
                default: return MainButtonCheckAgain;
            }
        }

        // ---------------------------------------------------------------- main window: notices

        public const string WarningEditedBlock = "Some QuietHosts lines in your hosts file were changed outside QuietHosts.";
        public const string WarningMalformedMarkers = "Some QuietHosts marker lines are damaged. They're fixed with your next change.";
        public const string WarningMicrosoftLinesOutside = "Another program added lines that block Microsoft addresses. Windows Security may reset your hosts file because of them, which also removes QuietHosts.";
        public const string WarningChangeNotFinished = "Your last change didn't finish, so your hosts file may be incomplete.";
        public const string WarningLegacyBlockToo = "Your hosts file still has the older block-ads.ps1 list. It's removed with your next change.";

        public static string WarningLargeFile(long bytesOutsideBlocks) =>
            "Your hosts file is large (" + FileSize(bytesOutsideBlocks) + "), mostly from other programs. Very large hosts files can slow Windows down.";

        /// <summary>The text for one warning flag ({size} from the status for LargeFile).</summary>
        public static string Warning(HostsWarnings warning, long bytesOutsideBlocks)
        {
            switch (warning)
            {
                case HostsWarnings.EditedBlock: return WarningEditedBlock;
                case HostsWarnings.MalformedMarkers: return WarningMalformedMarkers;
                case HostsWarnings.LargeFile: return WarningLargeFile(bytesOutsideBlocks);
                case HostsWarnings.MicrosoftLinesOutside: return WarningMicrosoftLinesOutside;
                case HostsWarnings.ChangeNotFinished: return WarningChangeNotFinished;
                case HostsWarnings.LegacyBlockToo: return WarningLegacyBlockToo;
                default: return string.Empty;
            }
        }

        public const string TipRunningFromDownloads = "Tip: QuietHosts is safer in a folder of its own than in Downloads.";
        public const string TipRunningFromTemp = "QuietHosts is running from a temporary folder. Copy QuietHosts.exe to a folder of its own and start it from there.";

        public static string Tip(RunLocationTips tip)
        {
            switch (tip)
            {
                case RunLocationTips.RunningFromDownloads: return TipRunningFromDownloads;
                case RunLocationTips.RunningFromTemp: return TipRunningFromTemp;
                default: return string.Empty;
            }
        }

        /// <summary>N6, while On: this exe ships a newer list than the one in the hosts file (new versions bring new lists).</summary>
        public static string TipNewerListInApp(string? listVersion) =>
            "This copy of QuietHosts has a newer block list (" + ListDate(listVersion) + ").";

        /// <summary>N3, while On: the hosts file has a newer list than this exe ships.</summary>
        public static string NoteNewerListInFile(string? listVersion) =>
            "Your hosts file has a newer block list (" + ListDate(listVersion) + "). Changes made with this copy put an older list back.";

        /// <summary>The notice actions (at most one per bar). The three buttons carry the shield; the links don't.</summary>
        public const string RepairButton = "Re&pair";
        /// <summary>N6: rewrites the block from this exe's list at the same level (one Windows prompt).</summary>
        public const string UseNewerListButton = "&Use newer list";
        /// <summary>N7: the same rewrite, which drops the older block-ads.ps1 list.</summary>
        public const string RemoveOlderListButton = "R&emove it now";
        public const string ShowLinesLink = "Show &lines";
        public const string GetNewestLink = "&Get the newest QuietHosts";
        /// <summary>The Downloads tip: How it works at "Keep QuietHosts.exe in a folder of its own".</summary>
        public const string WhyLink = "Wh&y?";

        /// <summary>"Show 2 more notices" under the third bar; "Show 1 more notice".</summary>
        public static string ShowMoreNotices(int count) =>
            count == 1 ? "Sh&ow 1 more notice" : "Sh&ow " + Number(count) + " more notices";

        // ---------------------------------------------------------------- main window: level cards, block list line, footer

        public const string LevelHeading = "Blocking level";
        /// <summary>Under the heading while QuietHosts can't read or change the hosts file (Can't read, Refused).</summary>
        public const string LevelNote = "You can change the level when QuietHosts can change your hosts file.";
        public const string StandardTitle = "Standard";
        public const string StrictTitle = "Strict";
        public const string RecommendedTag = "Recommended";

        /// <summary>One line in the card at 100% text (D19); the longer wording lives on in How it works.</summary>
        public const string StandardDescription = "Blocks common ads and trackers. Sites keep working.";

        /// <summary>
        /// One line in the card at 100% text (D19). It leads with the trade-off, and the count comes last, so "more" doesn't
        /// pull people toward Strict; "What Strict changes" has the details.
        /// </summary>
        public static string StrictDescription(int strictExtraCount) =>
            "Some sites may break. Blocks " + Count(strictExtraCount, "more address", "more addresses") + ".";

        public const string WhatStrictChangesLink = "&What Strict changes";
        /// <summary>Opens the Strict confirmation (its Use Strict carries the shield while protection is on).</summary>
        public const string UseStrictCardButton = "Use St&rict" + Ellipsis;
        public const string UseStandardCardButton = "Use Stan&dard";
        public const string InUseIndicator = "In use";
        public const string SelectedIndicator = "Selected";
        public const string RecommendedSuffix = ", recommended";
        public const string InUseSuffix = ", in use";
        public const string SelectedSuffix = ", selected";

        /// <summary>A level card's group name: "Standard, recommended, in use", "Strict, selected", "Strict".</summary>
        public static string LevelCardName(Level level, LevelCardState state)
        {
            string name = LevelName(level);
            if (level == Level.Standard) name += RecommendedSuffix;
            if (state == LevelCardState.InUse) name += InUseSuffix;
            else if (state == LevelCardState.Selected) name += SelectedSuffix;
            return name;
        }

        public static string BlockListFrom(string? listVersion) => "Block list from " + ListDate(listVersion);
        public const string CheckForNewerVersionLink = "Check for a &newer version";

        public const string FixButton = "&Fix a site or app" + Ellipsis;
        public const string UndoButton = "Undo &all changes" + Ellipsis;
        public const string AboutButton = "A&bout";

        // ---------------------------------------------------------------- changes and results

        public const string WaitingForPermission = "Waiting for your permission. If you don't see the Windows prompt, check the taskbar.";
        public const string ChangingHostsFile = "Changing the hosts file" + Ellipsis;

        /// <summary>The progress line for a stage. TakingLong keeps "Changing the hosts file…" and adds <see cref="ChangingSlow"/> after it.</summary>
        public static string Progress(LaunchStage stage) => stage == LaunchStage.WaitingForPermission ? WaitingForPermission : ChangingHostsFile;

        /// <summary>After "Changing the hosts file…" once the elevated run has gone on for 30 s. The window keeps waiting for it.</summary>
        public const string ChangingSlow = "This is taking longer than usual. If your antivirus asks about QuietHosts, that's this change.";

        /// <summary>
        /// Another QuietHosts run is changing the hosts file (seen when the window reads the file: at start-up, F5, Check again):
        /// change actions wait for it, and the window reads the file again when it ends.
        /// </summary>
        public const string OtherChangeRunning = "QuietHosts is already changing the hosts file. This window updates when it's done.";

        public const string ResultTurnOnOk = "Protection is on and working. Restart your browser to see the change.";
        public const string ResultSelfCheckFailed = "The list is in your hosts file, but QuietHosts couldn't confirm that Windows uses it yet. Restart your browser; if ads still show, restart your PC.";
        public const string ResultTurnOffOk = "Protection is off. Your level and allowed addresses are kept for next time.";
        public const string ResultUndoOk = "Everything QuietHosts added is removed. To uninstall, delete QuietHosts.exe.";
        public const string ResultDeclined = "Nothing was changed.";
        public const string ResultBusy = "QuietHosts was already changing the hosts file. Try again in a moment.";

        /// <summary>A level change while protection is on.</summary>
        public static string ResultLevelChanged(Level level) => "You're now using " + LevelName(level) + ". Restart your browser to see the change.";

        public static string ResultAllowed(int count) => "Allowed " + Addresses(count) + ". Restart your browser, then reload the page.";
        public static string ResultBlocked(int count) => "Blocked " + Addresses(count) + " again. Restart your browser to see the change.";

        /// <summary>After "Use newer list".</summary>
        public static string ResultListUpdated(string? listVersion) =>
            "Updated to the block list from " + ListDate(listVersion) + ". Restart your browser to see the change.";

        /// <summary>After a notice's Repair: <paramref name="on"/> when it rewrote the block (protection stays on).</summary>
        public static string ResultRepaired(bool on) =>
            on ? "Your hosts file is repaired. Restart your browser to see the change." : "Your hosts file is repaired.";

        /// <summary>After "Remove it now".</summary>
        public const string ResultOlderListRemoved = "The older block-ads.ps1 list is removed. Restart your browser to see the change.";

        /// <summary>R13: refused, but the file itself is fine: the change would make it larger than 16 MB.</summary>
        public const string ResultRefusedChange = "Nothing was changed: the change would make your hosts file larger than 16 MB.";

        /// <summary>R12: no bar (the status card explains the refusal); screen readers hear this.</summary>
        public static string ResultRefusedAnnouncement(string headline, string detail) => "Nothing was changed. " + headline + ". " + detail;

        /// <summary>Choosing a level while protection is off saves it without a prompt; screen readers hear this.</summary>
        public static string LevelSavedAnnouncement(Level level) =>
            LevelName(level) + " is selected. QuietHosts uses it when you turn protection on.";

        /// <summary>In the self-check result bar (R2).</summary>
        public const string CopySelfCheckDetailsLink = "&Copy details";

        // ---------------------------------------------------------------- error dialogs

        /// <summary>The title bar of the Strict dialogs, Undo, the error dialogs and the open-failure dialog (the question is the heading inside).</summary>
        public const string ErrorWindowTitle = "QuietHosts";
        /// <summary>The error dialog's glyph, for screen readers.</summary>
        public const string ErrorIconName = "Error";

        /// <summary>The error dialog's heading (it has no period of its own).</summary>
        public static string ErrorTitle(ResultKind kind)
        {
            switch (kind)
            {
                case ResultKind.FileLocked: return "The hosts file is in use";
                case ResultKind.AccessDenied: return "Your antivirus or Windows blocked the change";
                case ResultKind.Reverted: return "The change was undone right away";
                default: return "QuietHosts couldn't finish";
            }
        }

        /// <summary>A problem in the window itself: the error dialog's heading, and the first line of the start-up message box.</summary>
        public const string WindowErrorTitle = "Something went wrong";

        /// <param name="hostsFileChanged">For Unexpected: false = unchanged; true or null = may be incomplete.</param>
        public static string ErrorBody(ResultKind kind, bool? hostsFileChanged)
        {
            switch (kind)
            {
                case ResultKind.FileLocked:
                    return "Nothing was changed. Another program is using the hosts file. Wait a minute and try again; if it keeps happening, restart your PC.";
                case ResultKind.AccessDenied:
                    // No "trusted programs" advice and no "allow the change": the report's ethics table (no prompt coaching).
                    return "Nothing was changed. Some antivirus programs protect the hosts file. If yours asks about QuietHosts, that's this change.";
                case ResultKind.Reverted:
                    return "QuietHosts changed the hosts file, but another program put it back, most likely your antivirus. Look for a message from it about the hosts file.";
                default:
                    return hostsFileChanged == false ? UnexpectedUnchanged : UnexpectedMayBeIncomplete;
            }
        }

        public const string UnexpectedUnchanged = "Your hosts file wasn't changed. If this happens again, report it on GitHub and paste the details.";
        /// <summary>The file may be incomplete: Repair runs the same command again (D9).</summary>
        public const string UnexpectedMayBeIncomplete = "Your hosts file may be incomplete. Repair makes the same change again, or you can use Undo all changes.";

        /// <summary>A problem in the window itself (it never writes the hosts file).</summary>
        public const string WindowErrorBody = "QuietHosts ran into a problem, but your hosts file wasn't changed. If it keeps happening, report it on GitHub and paste the details.";

        public const string TryAgainButton = "&Try again";
        public const string CloseButton = "Close";
        public const string CancelButton = "Cancel";
        public const string ReportOnGitHubButton = "&Report on GitHub";
        public const string CopyDetailsLink = "&Copy details";
        public const string DetailsCopied = "Details copied. Paste them into your report.";
        public const string DetailsNotCopied = "QuietHosts couldn't copy the details. Try again in a moment.";

        // ---------------------------------------------------------------- open failure (a link or folder that didn't open)

        public const string LinkFailedTitle = "QuietHosts couldn't open the link";
        public const string LinkFailedBody = "Copy this address into your browser:";
        public const string FolderFailedTitle = "QuietHosts couldn't open the folder";
        public const string FolderFailedBody = "This is the folder:";
        public const string CopyButton = "&Copy";
        public const string TextCopied = "Copied.";

        // ---------------------------------------------------------------- Copy details

        public const string DetailsApp = "App";
        public const string DetailsLists = "Block lists";
        public const string DetailsWindows = "Windows";
        public const string DetailsAction = "Action";
        public const string DetailsResult = "Result";
        public const string DetailsExitCode = "Exit code";
        public const string DetailsNoExitCode = "none";
        public const string DetailsHostsFile = "Hosts file";
        public const string DetailsHostsUnchanged = "unchanged";
        public const string DetailsHostsChanged = "changed";
        public const string DetailsHostsUnknown = "unknown";
        public const string DetailsState = "State now";
        public const string DetailsError = "Error";
        public const string DetailsElevatedWindow = "Window runs as administrator";
        public const string DetailsLastRun = "Last change (from the registry)";
        public const string DetailsNoLastRun = "none recorded";
        public const string DetailsSelfCheck = "Self-check";
        public const string DetailsPassed = "passed";
        public const string DetailsFailed = "failed";
        public const string DetailsSelfCheckNotOn = "the hosts file has no QuietHosts block after the change";
        public const string DetailsSelfCheckNoAddress = "the QuietHosts block has no address to look up";
        public const string Yes = "yes";
        public const string No = "no";

        /// <summary>Copy details: Windows' text size, so a report shows whether the message font follows it.</summary>
        public const string DetailsTextSize = "Text size";

        /// <summary>"150% (message font 13.5 pt)".</summary>
        public static string DetailsTextSizeValue(int percent, float points) =>
            percent.ToString(CultureInfo.InvariantCulture) + "% (message font " + points.ToString("0.0", CultureInfo.InvariantCulture) + " pt)";

        /// <summary>The Fix dialog's site report: the level in use or selected.</summary>
        public const string DetailsLevel = "Level";
        /// <summary>The Fix dialog's site report: how many addresses the user allowed (the count only, never the names).</summary>
        public const string DetailsAllowedCount = "Allowed addresses";

        // ---------------------------------------------------------------- first run

        public const string FirstRunTitle = "Block ads and trackers on this PC";
        public const string FirstRunFact1Title = "Works in every browser and app";

        public static string FirstRunFact1Body(int standardCount) =>
            "QuietHosts adds " + AdAddresses(standardCount) + " to the Windows hosts file, so nothing on this PC can load them. It doesn't run in the background or go online.";

        public const string FirstRunFact2Title = "Windows asks before each change";

        /// <summary>While releases are unsigned (and when a signature can't be confirmed).</summary>
        public const string FirstRunFact2Unsigned = "Windows will say " + OpenQuote + "Unknown publisher" + CloseQuote +
            " because QuietHosts isn't signed yet. Say yes only if the prompt names QuietHosts and you just clicked a QuietHosts button.";

        /// <summary>Once releases are signed: the signer's name as Windows shows it.</summary>
        public static string FirstRunFact2Signed(string publisher) => "Windows shows " + OpenQuote + publisher + CloseQuote +
            " as the publisher. Say yes only if the prompt names QuietHosts and you just clicked a QuietHosts button.";

        public const string FirstRunFact3Title = "Easy to undo";
        public const string FirstRunFact3Body = "Turn it off or remove everything at any time. Lines added by you or other programs stay as they are.";
        public const string FirstRunLimit = "Video ads on YouTube and Twitch can't be blocked this way.";
        /// <summary>After the limit line: How it works at "What it can't block" (no browser).</summary>
        public const string FirstRunWhyLink = "&Why?";
        public const string FirstRunTrust = "Free and open source. No ads, no tracking, no account.";
        public const string FirstRunLegacy = "QuietHosts found an older list from block-ads.ps1. Turning on replaces it.";
        public const string FirstRunTurnOnButton = "&Turn on protection";
        public const string FirstRunNotNowButton = "Not &now";

        // ---------------------------------------------------------------- Strict confirmation

        public const string StrictConfirmTitle = "Use Strict blocking?";
        public const string StrictInfoTitle = "What Strict changes";

        /// <summary>The Strict dialogs' intro: the count, who Strict is for, then the effects as bullets.</summary>
        public static string StrictIntro(int strictExtraCount) =>
            "Strict blocks " + Count(strictExtraCount, "more ad and tracker address", "more ad and tracker addresses") +
            ". It's for people who want the most blocking and don't mind fixing a site now and then. With Strict:";

        public static readonly string[] StrictEffects =
        {
            "Links in ads, sponsored Google results and shopping emails may not open.",
            "Some videos, " + OpenQuote + "Recommended stories" + CloseQuote + " boxes and product boxes may stay empty.",
            "Some sites show " + OpenQuote + "ad blocker detected" + CloseQuote + " or have broken buttons, forms or checkout.",
            "Some apps, developer tools and ad or analytics dashboards may show errors.",
        };

        public const string StrictOutro = "You can switch back to Standard at any time.";
        public const string UseStrictButton = "&Use Strict";
        public const string Bullet = "\u2022";

        // ---------------------------------------------------------------- "Fix a site or app that isn't working"

        public const string FixTitle = "Fix a site or app that isn't working";
        public const string FixIntro = "Try these steps in order";

        public const string FixStep1Title = "Restart your browser";
        public const string FixStep1Body = "Close all its windows, open it again and reload the page.";
        /// <summary>Only while protection is on at Strict.</summary>
        public const string FixStep2Title = "Try Standard";
        public const string FixStep2Body = "Strict can break some sites.";
        public const string FixSwitchToStandardButton = "Switch to Stan&dard";
        public const string FixStepTestTitle = "Test with protection off";
        public const string FixStepTestBody = "If the page then works, turn protection on again and allow its address below.";
        /// <summary>The test turn-off (the main window then says it was a test, S2).</summary>
        public const string FixTurnOffButton = "Turn &off for now";
        public const string FixStepOffTitle = "Protection is off";
        /// <summary>Off after "Turn off for now" in this session.</summary>
        public const string FixStepOffAfterTestBody = "If the page works now, turn protection on again and allow its address below.";
        /// <summary>Off (and Removed): with protection off QuietHosts can't break the page, but browsers keep blocked addresses for a while (D13).</summary>
        public const string FixStepOffBody = "QuietHosts isn't blocking anything now. If the page still doesn't work after you restart your browser, QuietHosts isn't the cause.";
        public const string FixTurnOnButton = "Turn &on again";
        public const string FixTurnOnProtectionButton = "Turn &on protection";
        /// <summary>The older block-ads.ps1 list is in the hosts file without QuietHosts' block (Older list, or Off or Removed with it).</summary>
        public const string FixStepOlderListTitle = "Replace the older list";
        public const string FixStepOlderListBody = "Your hosts file still has the block-ads.ps1 list, which can cause this.";
        public const string FixReplaceOlderListButton = "Turn &on";
        /// <summary>Can't read and Refused: the window can't tell what the file blocks or change it.</summary>
        public const string FixStepCantChangeTitle = "QuietHosts can't change your hosts file right now";
        public const string FixStepCantChangeBody = "Close this window and follow the main window.";

        /// <summary>A step card's group name: "Step 1: Restart your browser".</summary>
        public static string FixStepName(int number, string title) => "Step " + Number(number) + ": " + title;

        /// <summary>The numeral in a step card's badge.</summary>
        public static string StepNumber(int number) => Number(number);

        // The short names of the steps shown, for the search box's description (spec 5.11).
        public const string FixStepShortRestart = "restart your browser";
        public const string FixStepShortStandard = "try Standard";
        public const string FixStepShortTest = "test with protection off";
        public const string FixStepShortTurnOn = "turn protection on again";
        public const string FixStepShortOlderList = "replace the older list";

        /// <summary>"Steps: restart your browser; try Standard; test with protection off. Then search for an address to allow."</summary>
        /// <param name="canAllow">QuietHosts can change the hosts file, so allowing an address is the next step.</param>
        public static string FixDescription(System.Collections.Generic.IEnumerable<string> steps, bool canAllow) =>
            "Steps: " + string.Join("; ", steps) + "." + (canAllow ? " Then search for an address to allow." : string.Empty);

        public const string FixReportPrompt = "Found the address that breaks a site?";
        public const string FixReportSiteLink = "&Report this site on GitHub";
        /// <summary>Copies the site report (versions, state, level, how many addresses are allowed; never their names).</summary>
        public const string FixCopyDetailsLink = "&Copy details";

        public const string FixSearchLabel = "&Search blocked addresses";
        public const string FixSearchAccessibleName = "Search blocked addresses";
        public const string FixSearchHint = "Site, company or address, for example yahoo";
        public static string FixShowOnlyAllowed(int allowedCount) => "Show only addresses you a&llowed (" + Number(allowedCount) + ")";
        public const string FixListAccessibleName = "Blocked and allowed addresses";

        public const string ColumnAddress = "Address";
        public const string ColumnCompany = "Company";
        public const string ColumnWhatItIs = "What it is";
        public const string ColumnLevel = "Level";
        public const string ColumnStatus = "Status";
        public const string NoneKnown = "None known";
        /// <summary>The address is in QuietHosts' block in the hosts file.</summary>
        public const string StatusBlocked = "Blocked";
        public const string StatusAllowed = "Allowed by you";
        /// <summary>QuietHosts' block doesn't have the address: protection is off, or the block came from another list.</summary>
        public const string StatusNotBlocked = "Not blocked";
        /// <summary>The hosts file can't be read, or the older block-ads.ps1 list may block the address.</summary>
        public const string StatusUnknown = "Unknown";

        public static string FixShowing(int shown, int total) => "Showing " + Number(shown) + " of " + Number(total);

        /// <summary>After the count, in a second label that isn't a live region: " \u00b7 2 selected".</summary>
        public static string FixSelectedSuffix(int selected) => " " + MiddleDot + " " + Number(selected) + " selected";

        /// <summary>The hover text of a row: the address and, if known, what stops working.</summary>
        public static string FixRowToolTip(string address, string note) => note.Length > 0 ? address + ": " + note : address;

        /// <summary>
        /// The line under the list for the selected row: the address, its company if known, its level, and the whole note, which
        /// the list doesn't show.
        /// </summary>
        public static string FixRowDetails(string address, string? company, string level, string note) =>
            "Known effects of " + address + " (" + (string.IsNullOrEmpty(company) ? string.Empty : company + ", ") + level + "): " + (note.Length > 0 ? note : NoneKnown);

        public const string FixNoSelection = "Select an address to see its known effects.";

        public static string FixNoMatch(string query) => "No blocked address matches " + OpenQuote + query + CloseQuote + ".";

        public const string FixNoMatchHint = "Searching for a website? Test with protection off instead.";

        /// <summary>The empty list's link to the last step card's button.</summary>
        public static string FixGoToStep(int number) => "&Go to step " + Number(number);

        public const string FixTurnOnToChange = "Turn protection on to allow or block addresses.";
        public const string FixCantChange = "QuietHosts can't change your hosts file right now.";
        public static string FixTooMany(int max) => "Select up to " + Number(max) + " addresses at a time.";
        /// <summary>The footer while blocked addresses are selected, and Allow's description.</summary>
        public const string FixConsequence = "Allowed addresses load in every browser and app on this PC.";

        public const string AllowButton = "&Allow";
        public const string BlockAgainButton = "&Block again";

        // ---------------------------------------------------------------- Undo all changes

        /// <summary>The Undo dialog's heading (the title bar says "QuietHosts").</summary>
        public const string UndoTitle = "Remove everything QuietHosts added?";

        public static string UndoBody(int blockedCount, bool hasLegacyBlock)
        {
            string text = blockedCount > 1
                ? "This removes all " + Number(blockedCount) + " blocked addresses from the hosts file and forgets your level and allowed addresses."
                : blockedCount == 1
                    ? "This removes 1 blocked address from the hosts file and forgets your level and allowed addresses."
                    : "This forgets your level and allowed addresses.";
            text += " Lines added by you or other programs stay as they are, and backups of the hosts file are kept.";
            if (hasLegacyBlock) text += " " + UndoLegacy;
            return text;
        }

        public const string UndoLegacy = "The older block-ads.ps1 list is removed too.";
        public const string RemoveEverythingButton = "&Remove everything";

        // ---------------------------------------------------------------- lines that block Microsoft addresses (notice N5)

        public const string MicrosoftLinesTitle = "Lines that block Microsoft addresses";
        public const string MicrosoftLinesIntro = "These lines are in your hosts file, outside the QuietHosts section. Windows Security may reset the whole file because of them. QuietHosts never changes them.";

        /// <summary>Under the box when the file has more such lines than it shows.</summary>
        public static string MicrosoftLinesMore(int count) => count == 1 ? "And 1 more line." : "And " + Number(count) + " more lines.";

        public const string HowToRemoveLink = "How to &remove them";
        public const string CopyLinesButton = "&Copy lines";
        public const string LinesCopied = "Lines copied.";
        public const string LinesNotCopied = "QuietHosts couldn't copy the lines. Try again in a moment.";

        // ---------------------------------------------------------------- About

        public const string AboutTitle = "About QuietHosts";
        public static string AboutVersion(string appVersion) => "Version " + appVersion;
        public const string AboutSummary = "Blocks ads and trackers for your whole PC, using the Windows hosts file.";

        public const string AboutBlockListHeading = "Block list";
        public const string AboutPrivacyHeading = "Privacy";
        public const string AboutHostsFileHeading = "Your hosts file";
        public const string AboutSignatureHeading = "Signature";
        public const string AboutLicenseHeading = "License and credits";
        public const string AboutHelpHeading = "Help";
        public const string AboutUninstallHeading = "Uninstall";

        public static string AboutLists(string? listVersion, int standardCount, int strictExtraCount) =>
            "From " + ListDate(listVersion) + ": " + Addresses(standardCount) + " in Standard, " + Number(strictExtraCount) + " more in Strict.";

        public const string AboutPrivacy = "QuietHosts never goes online and collects nothing. No ads, no tracking, no account.";
        public const string AboutBackups = "Before every change, QuietHosts saves a copy of your hosts file next to it.";
        public const string OpenBackupFolderLink = "Open &backup folder";
        public const string HowItWorksLink = "&How QuietHosts works";

        /// <summary>About's Signature section, from the exe's own signature (read offline, 2.8.18).</summary>
        public const string SignatureUnsigned = "This copy isn't digitally signed yet, so Windows shows " + OpenQuote + "Unknown publisher" + CloseQuote +
            ". Download QuietHosts only from the project page.";

        public static string SignatureSigned(string publisher) =>
            "This copy is digitally signed by " + publisher + ". Windows shows this name when it asks for your permission.";

        public const string SignatureUnverified = "This copy has a digital signature that Windows couldn't confirm. Download QuietHosts again, only from the project page.";
        public const string CheckDownloadLink = "How to &check your download";

        public const string AboutLicense = "Free and open source under the MIT License. Copyright \u00A9 2026 QuietHosts contributors.";
        public const string ViewLicenseLink = "View &license";
        public const string AboutCredits = "Our own block list, checked against public lists by Steven Black, HaGeZi, OISD, AdGuard, Peter Lowe and the EasyList authors. Thank you to their maintainers.";
        public const string ProjectPageLink = "&Project page";
        public const string ReportProblemLink = "&Report a problem";
        /// <summary>The README's "If something goes wrong" (the error dialogs' "Get help" moved here, D12).</summary>
        public const string AboutHelpLink = "If something goes &wrong";
        public const string AboutUninstall = "Click Undo all changes, then delete QuietHosts.exe.";

        public const string LicenseTitle = "QuietHosts license";
        public const string HowItWorksTitle = "How QuietHosts works";

        // ---------------------------------------------------------------- How it works (the long-form help, spec 6.9)

        public const string HowWhatHeading = "What QuietHosts does";
        public const string HowWhatBody = "QuietHosts adds lines such as " + OpenQuote + "0.0.0.0 pagead2.googlesyndication.com" + CloseQuote +
            " to the Windows hosts file. Windows reads that file before it looks up an address, so no browser or app on this PC can reach those addresses.";

        public const string HowAskHeading = "Why Windows asks for your permission";
        public const string HowAskBody = "The hosts file is a protected Windows file, so Windows asks before every change. Buttons that need this show the Windows shield. " +
            "Say yes only if the prompt names QuietHosts and you just clicked a QuietHosts button. Some PCs ask for your PIN, fingerprint or face instead of a Yes button. " +
            "If your account isn't an administrator, an administrator's password is needed.";

        public const string HowRestartHeading = "After a change, restart your browser";
        public const string HowRestartBody = "Browsers remember addresses for a few minutes. Close all browser windows and open the browser again to see a change. " +
            "QuietHosts clears the Windows address cache itself.";

        public const string HowCantBlockHeading = "What it can't block";
        public const string HowCantBlockBody = "YouTube video ads, Twitch stream ads and sponsored posts on social sites come from the same servers as the videos and posts, so blocking them would break those sites. For those, use an ad-blocking browser extension such as uBlock Origin Lite (Chrome, Edge) or uBlock Origin (Firefox). It works well alongside QuietHosts.";
        public const string HowCantBlockMore = "A hosts file blocks exact addresses only, can't hide empty ad boxes, and has no effect when a browser or app uses a proxy.";

        public const string HowBackupsHeading = "Backups and undo";
        public const string HowBackupsBody = "Before every change, QuietHosts saves a copy of your hosts file in the same folder (About > Open backup folder). " +
            "Undo all changes removes everything QuietHosts added and forgets its settings. Lines added by you or other programs stay as they are.";

        public const string HowFolderHeading = "Keep QuietHosts.exe in a folder of its own";
        public const string HowFolderBody = "Each change runs QuietHosts.exe with administrator rights. Downloads collects files from many places, and Windows could load one of them into QuietHosts. " +
            "A folder of its own, for example a new folder in Documents, keeps it apart.";

        public const string HowUninstallHeading = "How to uninstall";
        public const string HowUninstallBody = "Click Undo all changes, then delete QuietHosts.exe. The backups stay next to your hosts file, in case you need them.";

        /// <summary>Shown by "View license" only if the embedded LICENSE file is missing from the build.</summary>
        public const string LicenseFallback =
            "MIT License\n\n" +
            "Copyright (c) 2026 QuietHosts contributors\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n";

        // ---------------------------------------------------------------- links

        public static string ReleasesUrl => AppInfo.RepositoryUrl + "/releases";
        public static string ReportSiteUrl => AppInfo.RepositoryUrl + "/issues/new?template=site-broke.yml";
        public static string ReportBugUrl => AppInfo.RepositoryUrl + "/issues/new?template=bug.yml";
        public static string HelpUrl => AppInfo.RepositoryUrl + "#if-something-goes-wrong";
        public static string ProjectUrl => AppInfo.RepositoryUrl;
        /// <summary>S10's "How to do this": the README's section on saving the hosts file as UTF-8.</summary>
        public static string Utf8HelpUrl => AppInfo.RepositoryUrl + "#quiethosts-wont-change-your-hosts-file";
        /// <summary>The Microsoft-lines dialog's "How to remove them".</summary>
        public static string DefenderHelpUrl => AppInfo.RepositoryUrl + "#windows-defender-reports-hostsfilehijack";
        /// <summary>About's "How to check your download": the README's hash and attestation steps.</summary>
        public static string VerifyDownloadUrl => AppInfo.RepositoryUrl + "#verify-your-download";
    }
}
