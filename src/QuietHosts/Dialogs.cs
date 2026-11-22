using System;
using System.Collections.Generic;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>The Strict dialog's two uses (spec 6.2).</summary>
    internal enum StrictDialogMode
    {
        /// <summary>"Use Strict blocking?" from the Strict card's "Use Strict…": Use Strict (default) and Cancel.</summary>
        Confirm,
        /// <summary>"What Strict changes" from the Strict card's link: Close only.</summary>
        Info,
    }

    /// <summary>
    /// "Use Strict blocking?" (spec 6.2): who Strict is for, its four effects, and that Standard is one click back. As a
    /// confirmation it has Use Strict (default, initial focus; the shield while protection is on, because the click then
    /// starts the change) and Cancel; from "What Strict changes" it has Close only. Cancel, Esc and the title-bar X change
    /// nothing.
    /// </summary>
    internal sealed class StrictDialog : ThemedDialog
    {
        /// <param name="elevates">Confirm while protection is on: Use Strict starts an elevated change (it gets the shield).</param>
        public StrictDialog(UiContext ctx, StrictDialogMode mode, bool elevates) : base(Strings.ErrorWindowTitle)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            Mode = mode;
            Elevates = mode == StrictDialogMode.Confirm && elevates;
            string heading = mode == StrictDialogMode.Confirm ? Strings.StrictConfirmTitle : Strings.StrictInfoTitle;
            string intro = Strings.StrictIntro(ctx.Lists.StrictExtraCount);

            Heading = DialogParts.Heading(Ramp, heading);
            Intro = DialogParts.Text(Ramp, intro, 12);
            ThemedTable bullets = DialogParts.Bullets(Ramp, Strings.StrictEffects, Metrics.HeadingGap, DialogParts.ContentWidth, out ThemedLabel[] effects);
            Effects = effects;
            Outro = DialogParts.Text(Ramp, Strings.StrictOutro, 12);
            Ui.AddRow(Body, Heading);
            Ui.AddRow(Body, Intro);
            Ui.AddRow(Body, bullets);
            Ui.AddRow(Body, Outro);

            ThemedButton focus;
            if (mode == StrictDialogMode.Confirm)
            {
                UseStrictControl = new ThemedButton(Ramp, Strings.UseStrictButton, ButtonStyle.Accent) { Shield = Elevates, DialogResult = DialogResult.OK };
                CancelControl = new ThemedButton(Ramp, Strings.CancelButton) { DialogResult = DialogResult.Cancel };
                Footer.SetRight(UseStrictControl, CancelControl);
                AcceptButton = UseStrictControl;
                CancelButton = CancelControl;
                focus = UseStrictControl;
            }
            else
            {
                CancelControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
                Footer.SetRight(CancelControl);
                AcceptButton = CancelControl;
                CancelButton = CancelControl;
                focus = CancelControl;
            }
            var spoken = new List<string> { intro };
            spoken.AddRange(Strings.StrictEffects);
            spoken.Add(Strings.StrictOutro);
            Finish(focus, DialogParts.Spoken(heading, spoken.ToArray()));
        }

        /// <summary>Confirm or Info.</summary>
        public StrictDialogMode Mode { get; }

        /// <summary>Use Strict starts an elevated change (protection is on): its button carries the shield.</summary>
        public bool Elevates { get; }

        internal ThemedLabel Heading { get; }
        internal ThemedLabel Intro { get; }
        internal IReadOnlyList<ThemedLabel> Effects { get; }
        internal ThemedLabel Outro { get; }
        /// <summary>Confirm only: Use Strict.</summary>
        internal ThemedButton? UseStrictControl { get; }
        /// <summary>Cancel (Confirm) or Close (Info).</summary>
        internal ThemedButton CancelControl { get; }
    }

    /// <summary>
    /// "Remove everything QuietHosts added?" (spec 6.3): what goes and what stays. Remove everything always has the shield
    /// (Undo always starts an elevated run, D2); Cancel is the default and has the focus, so Enter right after opening
    /// cancels (U5).
    /// </summary>
    internal sealed class UndoDialog : ThemedDialog
    {
        public UndoDialog(HostsStatus status) : base(Strings.ErrorWindowTitle)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            string body = Strings.UndoBody(status.BlockedCount, status.HasLegacyBlock);
            Heading = DialogParts.Heading(Ramp, Strings.UndoTitle);
            Message = DialogParts.Text(Ramp, body, 12);
            Ui.AddRow(Body, Heading);
            Ui.AddRow(Body, Message);

            RemoveControl = new ThemedButton(Ramp, Strings.RemoveEverythingButton) { Shield = true, DialogResult = DialogResult.OK };
            CancelControl = new ThemedButton(Ramp, Strings.CancelButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(RemoveControl, CancelControl);
            AcceptButton = CancelControl;
            CancelButton = CancelControl;
            Finish(CancelControl, DialogParts.Spoken(Strings.UndoTitle, body));
        }

        internal ThemedLabel Heading { get; }
        internal ThemedLabel Message { get; }
        internal ThemedButton RemoveControl { get; }
        internal ThemedButton CancelControl { get; }
    }

    /// <summary>
    /// The error dialogs (spec 6.4): the error glyph beside the heading and the body, the "Copy details" link in the footer
    /// (every error dialog) and the buttons of the kind of error. Try again and Repair return Retry: the change runs again
    /// with a new Windows prompt. Close (also Esc and the title-bar X) is the Cancel button everywhere. Declining the prompt
    /// never opens one (E4).
    /// </summary>
    internal sealed class ErrorDialog : ThemedDialog
    {
        private const int GlyphEpx = 32;
        private const int GlyphGap = 16;
        private const int ColumnWidth = DialogParts.ContentWidth - GlyphEpx - GlyphGap;
        private readonly UiContext _ctx;
        private readonly Func<string> _details;

        /// <summary>The buttons of each kind of error (6.4's table).</summary>
        private enum Buttons
        {
            /// <summary>FileLocked, AccessDenied, Reverted: Try again · Close.</summary>
            TryAgain,
            /// <summary>Unexpected, the file unchanged: Try again · Report on GitHub · Close.</summary>
            TryAgainAndReport,
            /// <summary>Unexpected, the file may be incomplete: Repair · Report on GitHub · Close.</summary>
            RepairAndReport,
            /// <summary>A problem in the window itself: Report on GitHub · Close (the default).</summary>
            WindowProblem,
        }

        /// <summary>An error result of a change.</summary>
        /// <param name="hostsFileChanged">For Unexpected: false = unchanged (Try again); true or null = may be incomplete (Repair).</param>
        public ErrorDialog(UiContext ctx, ResultKind kind, bool? hostsFileChanged, Func<string> details)
            : this(ctx, Strings.ErrorTitle(kind), Strings.ErrorBody(kind, hostsFileChanged), ButtonsFor(kind, hostsFileChanged), details)
        {
        }

        /// <summary>A problem in the window itself: no Try again.</summary>
        public ErrorDialog(UiContext ctx, Exception error)
            : this(ctx, Strings.WindowErrorTitle, Strings.WindowErrorBody, Buttons.WindowProblem, () => DetailsReport.Build(ctx, null, null, null, error))
        {
        }

        private ErrorDialog(UiContext ctx, string heading, string body, Buttons buttons, Func<string> details) : base(Strings.ErrorWindowTitle)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _details = details ?? throw new ArgumentNullException(nameof(details));
            UiScale s = Scale;

            // The glyph (32 epx, top-aligned with the heading), 16 epx, then the heading, the body and the copy feedback.
            ErrorGlyph = new GlyphIcon(Glyph.Error, GlyphEpx, ThemeToken.CriticalGlyph, Strings.ErrorIconName) { Margin = new Padding(0, 0, s.Px(GlyphGap), 0) };
            Heading = DialogParts.Heading(Ramp, heading, ColumnWidth);
            Message = DialogParts.Text(Ramp, body, 12, widthEpx: ColumnWidth);
            Feedback = DialogParts.Text(Ramp, string.Empty, Metrics.ButtonGap, TextRole.Secondary, ColumnWidth);
            Feedback.Visible = false;
            var column = new ThemedTable(1);
            Ui.AddRow(column, Heading);
            Ui.AddRow(column, Message);
            Ui.AddRow(column, Feedback);
            var row = new ThemedTable(2);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Ui.AddRow(row, ErrorGlyph, column);
            Ui.AddRow(Body, row);

            CopyLink = new ThemedLink(Ramp, Strings.CopyDetailsLink);
            CopyLink.Invoked += (o, e) => CopyDetails();
            Footer.SetLeft(CopyLink);

            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, buttons == Buttons.WindowProblem ? ButtonStyle.Accent : ButtonStyle.Standard)
            {
                DialogResult = DialogResult.Cancel,
            };
            CancelButton = CloseControl;
            var right = new List<Control>();
            ThemedButton focus;
            if (buttons == Buttons.WindowProblem)
            {
                focus = CloseControl;
            }
            else
            {
                // Try again and Repair start the same change again: a new Windows prompt (the shield), and Retry for the loop.
                RetryControl = new ThemedButton(Ramp, buttons == Buttons.RepairAndReport ? Strings.RepairButton : Strings.TryAgainButton, ButtonStyle.Accent)
                {
                    Shield = true,
                    DialogResult = DialogResult.Retry,
                };
                right.Add(RetryControl);
                focus = RetryControl;
            }
            if (buttons != Buttons.TryAgain)
            {
                ReportControl = new ThemedButton(Ramp, Strings.ReportOnGitHubButton) { External = true };
                ReportControl.Click += (o, e) => OpenUrl(Strings.ReportBugUrl);
                right.Add(ReportControl);
            }
            right.Add(CloseControl);
            Footer.SetRight(right.ToArray());
            AcceptButton = focus;
            Finish(focus, DialogParts.Spoken(heading, body));
        }

        private static Buttons ButtonsFor(ResultKind kind, bool? hostsFileChanged)
        {
            if (kind != ResultKind.Unexpected) return Buttons.TryAgain;
            return hostsFileChanged == false ? Buttons.TryAgainAndReport : Buttons.RepairAndReport;
        }

        /// <summary>The error glyph (role Graphic, named "Error").</summary>
        internal GlyphIcon ErrorGlyph { get; }
        internal ThemedLabel Heading { get; }
        internal ThemedLabel Message { get; }
        /// <summary>"Details copied. …" after Copy details (or that it failed).</summary>
        internal ThemedLabel Feedback { get; }
        internal ThemedLink CopyLink { get; }
        /// <summary>Try again or Repair (null for a problem in the window itself).</summary>
        internal ThemedButton? RetryControl { get; }
        /// <summary>Report on GitHub (Unexpected and a problem in the window itself).</summary>
        internal ThemedButton? ReportControl { get; }
        internal ThemedButton CloseControl { get; }

        /// <summary>The kinds whose dialog offers Try again (or Repair): every error dialog of a change.</summary>
        public static bool IsRetryable(ResultKind kind) =>
            kind == ResultKind.AccessDenied || kind == ResultKind.FileLocked || kind == ResultKind.Reverted || kind == ResultKind.Unexpected;

        /// <summary>Shows the dialog for an error result; DialogResult.Retry means "Try again" (or Repair).</summary>
        public static DialogResult ShowForResult(IWin32Window? owner, UiContext ctx, LaunchResult result, Func<string> details)
        {
            using (var d = new ErrorDialog(ctx, result.Kind, result.HostsFileChanged, details))
            {
                return d.ShowDialog(owner);
            }
        }

        public static void ShowWindowError(IWin32Window? owner, UiContext ctx, Exception error)
        {
            using (var d = new ErrorDialog(ctx, error)) d.ShowDialog(owner);
        }

        private void CopyDetails()
        {
            bool copied;
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewClipboard != null) copied = PreviewClipboard(_details());
            else
#endif
                // A preview copies nothing and shows what a copy that worked would.
                copied = _ctx.Preview || DetailsReport.TryCopy(_details());
            Feedback.Text = copied ? Strings.DetailsCopied : Strings.DetailsNotCopied;
            Feedback.Visible = true;
            Ui.Announce(this, Feedback.Text, Announcement.Result);
            if (IsHandleCreated) FitToWorkingArea(OwnerWorkingArea());
        }

        private void OpenUrl(string url)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewOpenUrl != null)
            {
                PreviewOpenUrl(url);
                return;
            }
#endif
            Links.OpenUrl(_ctx, this, url);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: Copy details, as if clicked.</summary>
        internal void PreviewCopyDetails() => CopyDetails();

        /// <summary>For tests: the clipboard (returns whether the copy worked).</summary>
        internal Func<string, bool>? PreviewClipboard { get; set; }

        /// <summary>For tests: links open here instead of in the browser.</summary>
        internal Action<string>? PreviewOpenUrl { get; set; }
#endif
    }

    /// <summary>
    /// A link or folder that didn't open (spec 6.5, in place of a message box): the address or folder in a read-only box the
    /// user can select, Copy (the line under the box says whether it worked) and Close (the default).
    /// </summary>
    internal sealed class OpenFailedDialog : ThemedDialog
    {
        /// <summary>The address box shows this many lines at most, then scrolls.</summary>
        internal const int MaxTargetLines = 3;
        private readonly UiContext _ctx;
        private readonly string _target;

        public OpenFailedDialog(UiContext ctx, string target, bool isFolder) : base(Strings.ErrorWindowTitle)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _target = target ?? string.Empty;
            string heading = isFolder ? Strings.FolderFailedTitle : Strings.LinkFailedTitle;
            string body = isFolder ? Strings.FolderFailedBody : Strings.LinkFailedBody;
            Heading = DialogParts.Heading(Ramp, heading);
            Message = DialogParts.Text(Ramp, body, 12);
            // Named by the body without its colon: a text box's text is its content, not a name. The address wraps, so all of
            // it shows (up to three lines, then the box scrolls): a long one cut at the box's edge would be copied wrong by hand.
            Target = new CodeBox(Ramp, TextStyle.Body, multiline: false, DialogParts.ContentWidth, 0, body.TrimEnd(':', ' '), wrapLines: MaxTargetLines)
            {
                Margin = new Padding(0, Scale.Px(Metrics.HeadingGap), 0, 0),
            };
            Target.SetText(_target);
            Feedback = DialogParts.Text(Ramp, string.Empty, 4, TextRole.Secondary);
            Feedback.Visible = false;
            Ui.AddRow(Body, Heading);
            Ui.AddRow(Body, Message);
            Ui.AddRow(Body, Target);
            Ui.AddRow(Body, Feedback);

            CopyControl = new ThemedButton(Ramp, Strings.CopyButton);
            CopyControl.Click += (o, e) => CopyTarget();
            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(CopyControl, CloseControl);
            AcceptButton = CloseControl;
            CancelButton = CloseControl;
            // The address itself follows, so a screen reader user hears it without going to the box.
            Finish(CloseControl, DialogParts.Spoken(heading, body, _target));
        }

        internal ThemedLabel Heading { get; }
        internal ThemedLabel Message { get; }
        /// <summary>The address or folder, read-only and selectable.</summary>
        internal CodeBox Target { get; }
        /// <summary>"Copied." after Copy (or that it failed).</summary>
        internal ThemedLabel Feedback { get; }
        internal ThemedButton CopyControl { get; }
        internal ThemedButton CloseControl { get; }

        private void CopyTarget()
        {
            bool copied;
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewClipboard != null) copied = PreviewClipboard(_target);
            else
#endif
                copied = _ctx.Preview || DetailsReport.TryCopy(_target);
            Feedback.Text = copied ? Strings.TextCopied : Strings.DetailsNotCopied;
            Feedback.Visible = true;
            Ui.Announce(this, Feedback.Text, Announcement.Result);
            if (IsHandleCreated) FitToWorkingArea(OwnerWorkingArea());
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: Copy, as if clicked.</summary>
        internal void PreviewCopy() => CopyTarget();

        /// <summary>For tests: the clipboard (returns whether the copy worked).</summary>
        internal Func<string, bool>? PreviewClipboard { get; set; }
#endif
    }
}
