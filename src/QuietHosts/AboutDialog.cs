using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// "About QuietHosts" (spec 6.7): the app icon, name and version, the summary, then the sections Block list, Privacy, Your
    /// hosts file, Signature (from the exe's own signature, read offline), License and credits, Help and Uninstall, each with
    /// its links. Close is the default, has the focus and is Esc.
    /// </summary>
    internal sealed class AboutDialog : ThemedDialog
    {
        private readonly AppController _c;
        private readonly List<ThemedLink> _links = new List<ThemedLink>();

        public AboutDialog(AppController controller) : base(Strings.AboutTitle)
        {
            _c = controller ?? throw new ArgumentNullException(nameof(controller));
            UiContext ctx = controller.Context;
            BlockLists lists = ctx.Lists;
            UiScale s = Scale;
            // The body's left padding leaves a link's 4 epx padding before its text, so link text lines up with the text above;
            // every other row is indented by those 4 epx.
            int ringRoom = s.Px(4);
            int pad = s.Px(Metrics.PagePadding);
            Body.Padding = new Padding(pad - ringRoom, pad, pad, pad);

            // ---- the header: the icon, 16 epx, the name and the version
            Icon48 = new AppIconImage(ctx.ExePath) { Margin = new Padding(ringRoom, 0, s.Px(16), 0) };
            AppName = new ThemedLabel(Ramp, Strings.AppName, TextStyle.Subtitle);
            Version = new ThemedLabel(Ramp, Strings.AboutVersion(AppInfo.Version), TextStyle.Body, TextRole.Secondary);
            var nameColumn = new ThemedTable(1);
            Ui.AddRow(nameColumn, AppName);
            Ui.AddRow(nameColumn, Version);
            var header = new ThemedTable(2);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Ui.AddRow(header, Icon48, nameColumn);
            Ui.AddRow(Body, header);
            Ui.AddRow(Body, Indented(DialogParts.Text(Ramp, Strings.AboutSummary, 12)));

            // ---- the sections: 24 epx before the first, 16 between
            AddSection(Strings.AboutBlockListHeading, 24, Strings.AboutLists(lists.Version, lists.StandardCount, lists.StrictExtraCount),
                Link(Strings.CheckForNewerVersionLink, true, () => OpenUrl(Strings.ReleasesUrl)));
            AddSection(Strings.AboutPrivacyHeading, 16, Strings.AboutPrivacy);
            AddSection(Strings.AboutHostsFileHeading, 16, Strings.AboutBackups,
                Link(Strings.OpenBackupFolderLink, false, () => OpenFolder(_c.Status.HostsFolder)),
                Link(Strings.HowItWorksLink, false, ShowHowItWorks));
            SignatureText = AddSection(Strings.AboutSignatureHeading, 16, SignatureLine(ctx.Signature.Value),
                Link(Strings.CheckDownloadLink, true, () => OpenUrl(Strings.VerifyDownloadUrl)));
            AddSection(Strings.AboutLicenseHeading, 16, Strings.AboutLicense,
                Link(Strings.ViewLicenseLink, false, ShowLicense));
            Ui.AddRow(Body, Indented(DialogParts.Text(Ramp, Strings.AboutCredits, Metrics.ButtonGap)));
            AddSection(Strings.AboutHelpHeading, 16, null,
                Link(Strings.ProjectPageLink, true, () => OpenUrl(Strings.ProjectUrl)),
                Link(Strings.ReportProblemLink, true, () => OpenUrl(Strings.ReportBugUrl)),
                Link(Strings.AboutHelpLink, true, () => OpenUrl(Strings.HelpUrl)));
            AddSection(Strings.AboutUninstallHeading, 16, Strings.AboutUninstall);

            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(CloseControl);
            AcceptButton = CloseControl;
            CancelButton = CloseControl;
            Finish(CloseControl, DialogParts.Spoken(Strings.AppName, Strings.AboutVersion(AppInfo.Version), Strings.AboutSummary));
        }

        internal AppIconImage Icon48 { get; }
        internal ThemedLabel AppName { get; }
        internal ThemedLabel Version { get; }
        /// <summary>The Signature section's text (it follows the exe's signature).</summary>
        internal ThemedLabel SignatureText { get; }
        /// <summary>The section headings, in order.</summary>
        internal List<ThemedLabel> SectionHeadings { get; } = new List<ThemedLabel>();
        /// <summary>Every link, in order.</summary>
        internal IReadOnlyList<ThemedLink> AllLinks => _links;
        internal ThemedButton CloseControl { get; }

        /// <summary>The Signature section's sentence for the exe's signature (2.8.18).</summary>
        public static string SignatureLine(SignatureState signature)
        {
            switch (signature.Kind)
            {
                case SignatureKind.Signed when !string.IsNullOrEmpty(signature.Publisher):
                    return Strings.SignatureSigned(signature.Publisher!);
                case SignatureKind.Unsigned:
                    return Strings.SignatureUnsigned;
                default:
                    return Strings.SignatureUnverified;
            }
        }

        private Control Indented(Control c)
        {
            Padding m = c.Margin;
            c.Margin = new Padding(m.Left + Scale.Px(4), m.Top, m.Right, m.Bottom);
            return c;
        }

        /// <summary>A heading (Body Strong), 4 epx, its text (if any), then 8 epx and its links in a wrapping row, 16 epx apart.</summary>
        private ThemedLabel AddSection(string heading, int gapEpx, string? text, params ThemedLink[] links)
        {
            var title = new ThemedLabel(Ramp, heading, TextStyle.BodyStrong, TextRole.Primary, DialogParts.ContentWidth) { Margin = new Padding(0, Scale.Px(gapEpx), 0, 0) };
            SectionHeadings.Add(title);
            Ui.AddRow(Body, Indented(title));
            ThemedLabel? body = null;
            if (text != null)
            {
                body = DialogParts.Text(Ramp, text, 4);
                Ui.AddRow(Body, Indented(body));
            }
            if (links.Length > 0)
            {
                ThemedFlow row = DialogParts.LinkRow(Ramp, 16, links);
                row.MaximumSize = new Size(Scale.Px(DialogParts.ContentWidth) + Scale.Px(4), 0);
                row.Margin = new Padding(0, Scale.Px(text != null ? Metrics.ButtonGap : 4), 0, 0);
                Ui.AddRow(Body, row);
            }
            return body ?? title;
        }

        private ThemedLink Link(string text, bool external, Action invoked)
        {
            var link = new ThemedLink(Ramp, text) { External = external };
            link.Invoked += (o, e) => invoked();
            _links.Add(link);
            return link;
        }

        private void ShowHowItWorks()
        {
            using (var d = new HowItWorksDialog(_c.Context, HowItWorksSection.Top)) d.ShowDialog(this);
        }

        private void ShowLicense()
        {
            using (var d = new LicenseDialog()) d.ShowDialog(this);
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
            Links.OpenUrl(_c.Context, this, url);
        }

        private void OpenFolder(string folder)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewOpenUrl != null)
            {
                PreviewOpenUrl(folder);
                return;
            }
#endif
            Links.OpenFolder(_c.Context, this, folder);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For tests: links and the folder open here instead of in the browser or Explorer.</summary>
        internal Action<string>? PreviewOpenUrl { get; set; }
#endif
    }

    /// <summary>
    /// "View license" (spec 6.8): the embedded LICENSE file in a read-only, word-wrapping box in a card. The box has the
    /// focus (arrow keys scroll it), with the caret at the start; Close is the default.
    /// </summary>
    internal sealed class LicenseDialog : ThemedDialog
    {
        public const string ResourceName = "QuietHosts.LICENSE.txt";
        private const int BoxPadding = 12;

        public LicenseDialog() : base(Strings.LicenseTitle)
        {
            Box = new CodeBox(Ramp, TextStyle.Body, multiline: true, DialogParts.ContentWidth, 300 + 2 * BoxPadding, Strings.LicenseTitle, BoxPadding, wordWrap: true);
            Box.TextBox.Text = ForDisplay(LoadText());
            Ui.AddRow(Body, Box);
            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(CloseControl);
            AcceptButton = CloseControl;
            CancelButton = CloseControl;
            Finish(Box.TextBox, null);
        }

        /// <summary>The card with the license text (408 × 300 epx inside a 12 epx padding).</summary>
        internal CodeBox Box { get; }
        internal ThemedButton CloseControl { get; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Box.TextBox.SelectionStart = 0;
            Box.TextBox.SelectionLength = 0;
        }

        /// <summary>The LICENSE file embedded in the exe, or the MIT text if the build had none.</summary>
        public static string LoadText()
        {
            using (Stream? s = typeof(LicenseDialog).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (s == null) return Strings.LicenseFallback;
                using (var reader = new StreamReader(s, new UTF8Encoding(false), true))
                {
                    string text = reader.ReadToEnd();
                    return text.Trim().Length == 0 ? Strings.LicenseFallback : text;
                }
            }
        }

        /// <summary>Joins the hard-wrapped lines of each paragraph, so the text wraps to the box.</summary>
        public static string ForDisplay(string text)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var paragraphs = new List<string>();
            var current = new StringBuilder();
            foreach (string raw in normalized.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    if (current.Length > 0) paragraphs.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(line);
            }
            if (current.Length > 0) paragraphs.Add(current.ToString());
            return string.Join("\r\n\r\n", paragraphs);
        }
    }

    /// <summary>How it works' sections (spec 6.9), in their order; <see cref="Top"/> is the start of the dialog.</summary>
    internal enum HowItWorksSection
    {
        Top,
        WhatItDoes,
        WhyWindowsAsks,
        RestartBrowser,
        /// <summary>"What it can't block": the first run's "Why?".</summary>
        CantBlock,
        Backups,
        /// <summary>"Keep QuietHosts.exe in a folder of its own": the Downloads tip's "Why?".</summary>
        OwnFolder,
        Uninstall,
    }

    /// <summary>
    /// "How QuietHosts works" (spec 6.9), the long-form help: seven sections, each a heading and its text. It opens scrolled to
    /// <see cref="Start"/> when its content scrolls; the content has no focusable control, so PageUp, PageDown, Ctrl+Home and
    /// Ctrl+End scroll it while Close has the focus. Close's description is the start section.
    /// </summary>
    internal sealed class HowItWorksDialog : ThemedDialog
    {
        private readonly Dictionary<HowItWorksSection, ThemedLabel> _headings = new Dictionary<HowItWorksSection, ThemedLabel>();

        /// <param name="start">The section shown at the top of the visible area (Top: no scroll).</param>
        public HowItWorksDialog(UiContext ctx, HowItWorksSection start = HowItWorksSection.Top) : base(Strings.HowItWorksTitle)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            Start = start;
            string? spoken = null;
            bool first = true;
            foreach ((HowItWorksSection section, string heading, string[] texts) in Sections())
            {
                var title = new ThemedLabel(Ramp, heading, TextStyle.BodyStrong, TextRole.Primary, DialogParts.ContentWidth)
                {
                    Margin = new Padding(0, first ? 0 : Scale.Px(SectionGapEpx), 0, 0),
                };
                Ui.AddRow(Body, title);
                _headings[section] = title;
                for (int i = 0; i < texts.Length; i++) Ui.AddRow(Body, DialogParts.Text(Ramp, texts[i], i == 0 ? 4 : Metrics.ButtonGap));
                if (section == (start == HowItWorksSection.Top ? HowItWorksSection.WhatItDoes : start)) spoken = DialogParts.Spoken(heading, texts);
                first = false;
            }
            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent) { DialogResult = DialogResult.Cancel };
            Footer.SetRight(CloseControl);
            AcceptButton = CloseControl;
            CancelButton = CloseControl;
            Finish(CloseControl, spoken);
        }

        /// <summary>The section the dialog was opened at.</summary>
        public HowItWorksSection Start { get; }

        internal ThemedButton CloseControl { get; }

        /// <summary>A section's heading (Top: the first section's).</summary>
        internal ThemedLabel HeadingOf(HowItWorksSection section) => _headings[section == HowItWorksSection.Top ? HowItWorksSection.WhatItDoes : section];

        /// <summary>The sections in order: the heading and its paragraphs.</summary>
        internal static IEnumerable<(HowItWorksSection Section, string Heading, string[] Texts)> Sections()
        {
            yield return (HowItWorksSection.WhatItDoes, Strings.HowWhatHeading, new[] { Strings.HowWhatBody });
            yield return (HowItWorksSection.WhyWindowsAsks, Strings.HowAskHeading, new[] { Strings.HowAskBody });
            yield return (HowItWorksSection.RestartBrowser, Strings.HowRestartHeading, new[] { Strings.HowRestartBody });
            yield return (HowItWorksSection.CantBlock, Strings.HowCantBlockHeading, new[] { Strings.HowCantBlockBody, Strings.HowCantBlockMore });
            yield return (HowItWorksSection.Backups, Strings.HowBackupsHeading, new[] { Strings.HowBackupsBody });
            yield return (HowItWorksSection.OwnFolder, Strings.HowFolderHeading, new[] { Strings.HowFolderBody });
            yield return (HowItWorksSection.Uninstall, Strings.HowUninstallHeading, new[] { Strings.HowUninstallBody });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ScrollToStart();
        }

        /// <summary>
        /// The start section's heading at the top of the visible area (when the content scrolls; Top doesn't scroll). A
        /// section near the end can't scroll that far by itself: the content then gets that much room after its last section,
        /// so the heading still reaches the top and no line is cut at the top edge (6.9).
        /// </summary>
        private void ScrollToStart()
        {
            ScrollHost? host = ContentHost;
            if (host == null || !host.Scrolls || Start == HowItWorksSection.Top) return;
            host.ScrollTo(StartScrollTop(host));
        }

        /// <summary>
        /// Where the content scrolls to for the start section: its heading at the top of the visible area, with the 16 epx gap
        /// that is above every heading (so no line of the section before shows cut at the top edge). The content first gets
        /// the room after its last section that this needs.
        /// </summary>
        private int StartScrollTop(ScrollHost host)
        {
            ThemedLabel heading = HeadingOf(Start);
            int top = Math.Max(0, heading.Top - Scale.Px(SectionGapEpx));
            int missing = top + host.ClientSize.Height - Body.Height;
            if (missing > 0)
            {
                Padding p = Body.Padding;
                Body.Padding = new Padding(p.Left, p.Top, p.Right, p.Bottom + missing);
                host.PerformLayout();
            }
            return top;
        }

        /// <summary>The gap above each section's heading but the first.</summary>
        private const int SectionGapEpx = 16;

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For tests: scroll to the start section as opening the dialog does.</summary>
        internal void PreviewScrollToStart() => ScrollToStart();

        /// <summary>For tests: the content's scroll position from the top, in pixels.</summary>
        internal int PreviewScrollTop => ContentHost?.ScrollTop ?? 0;
#endif
    }
}
