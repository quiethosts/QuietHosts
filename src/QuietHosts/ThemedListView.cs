using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>One cell a <see cref="CellPainter"/> may paint: where, on which row state, in which colors.</summary>
    internal sealed class ListCell
    {
        public ListCell(Graphics graphics, ListViewItem item, int column, Rectangle bounds, Rectangle content, bool selected, bool hot,
            Theme theme, TypeRamp ramp)
        {
            Graphics = graphics;
            Item = item;
            Column = column;
            Bounds = bounds;
            Content = content;
            Selected = selected;
            Hot = hot;
            Theme = theme;
            Ramp = ramp;
        }

        public Graphics Graphics { get; }
        public ListViewItem Item { get; }
        public int Column { get; }
        /// <summary>The whole cell.</summary>
        public Rectangle Bounds { get; }
        /// <summary>The cell inside its padding (16 epx left in the first column, 12 in the others, 8 right).</summary>
        public Rectangle Content { get; }
        public bool Selected { get; }
        public bool Hot { get; }
        /// <summary>The row has a backplate (hovered or selected): pills use their selected-row look.</summary>
        public bool Highlighted => Selected || Hot;
        public Theme Theme { get; }
        public TypeRamp Ramp { get; }
        /// <summary>Primary text on this row (HighlightText on a highlighted row in high contrast).</summary>
        public Color TextColor => Highlighted ? Theme.ListTextSelected : Theme.TextPrimary;
        /// <summary>Secondary text on this row.</summary>
        public Color SecondaryTextColor => Highlighted ? Theme.ListSecondaryTextSelected : Theme.TextSecondary;
    }

    /// <summary>Paints one cell's content (pills, glyphs); returns false to let the list draw the plain text.</summary>
    internal delegate bool CellPainter(ListCell cell);

    /// <summary>
    /// The owner-drawn address list (spec 2.8.14): 32 epx rows with rounded hover and selection backplates, a selection bar,
    /// a Caption header and a focus ring on the focused row. Keyboard and UI Automation are the native list's. It knows no
    /// QuietHosts columns: the owner passes a <see cref="CellPainter"/>. Always give it an AccessibleName.
    /// <para>
    /// The list paints each row itself from the native custom-draw notification and tells the list to draw nothing of its
    /// own (CDRF_SKIPDEFAULT). WinForms' DrawItem event can't do that in Details view (it asks for the sub-items instead),
    /// and the native list then paints its theme's selection over the row: in dark mode a translucent fill and a frame
    /// that darken the backplate. After the list has painted, the empty area below the last row is filled with the card
    /// color, which hides the column lines the dark theme draws there.
    /// </para>
    /// </summary>
    internal sealed class ThemedListView : ListView, IThemed
    {
        private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E;
        private const int NM_CUSTOMDRAW = -12;
        private const int CDDS_PREPAINT = 0x00000001, CDDS_POSTPAINT = 0x00000002, CDDS_ITEM = 0x00010000;
        private const int CDDS_ITEMPREPAINT = CDDS_ITEM | CDDS_PREPAINT;
        private const int CDRF_DODEFAULT = 0x0, CDRF_SKIPDEFAULT = 0x4, CDRF_NOTIFYPOSTPAINT = 0x10, CDRF_NOTIFYITEMDRAW = 0x20;

        private readonly TypeRamp _ramp;
        private ImageList? _rows;
        private Font? _fallbackFont;
        private int _hot = -1;
        private bool _rowChecked;

        public ThemedListView(TypeRamp ramp)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            View = View.Details;
            FullRowSelect = true;
            MultiSelect = true;
            HideSelection = false;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            // The header is drawn through DrawColumnHeader; the rows through the custom-draw notification (see WndProc).
            OwnerDraw = true;
            // Sets LVS_EX_DOUBLEBUFFER (verified).
            DoubleBuffered = true;
            BorderStyle = BorderStyle.None;
            ShowItemToolTips = true;
            Font = ramp.Body;
            Margin = Padding.Empty;
            RowHeight = Math.Min(256, ramp.TextDriven(Metrics.ListRow, TextStyle.Body, Metrics.ButtonPadY));
            // The native row is the small image's height plus 1 px; set before any item is added.
            _rows = new ImageList { ImageSize = new Size(1, RowHeight - 1) };
            SmallImageList = _rows;
            ApplyTheme(ThemeManager.Current);
        }

        /// <summary>The row height in pixels: max(Px(32), Body line + Px(12)).</summary>
        public int RowHeight { get; }

        /// <summary>Paints cell content (pills, glyphs); null draws every cell as plain text.</summary>
        public CellPainter? CellPainter { get; set; }

        /// <summary>The image-list trick didn't set the row height here, so the list's font does (2.8.14's fallback).</summary>
        internal bool UsesFontFallback { get; private set; }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.CardBackground;
            ForeColor = theme.TextPrimary;
            ApplyNativeThemes(theme);
            Invalidate();
        }

        private void ApplyNativeThemes(Theme theme)
        {
            if (!IsHandleCreated) return;
            NativeTheme.SetTheme(Handle, theme.NativeThemeName);
            IntPtr header = NativeTheme.ListHeader(Handle);
            if (header != IntPtr.Zero)
            {
                NativeTheme.SetTheme(header, theme.HeaderThemeName);
                NativeTheme.Repaint(header);
            }
            IntPtr tips = NativeTheme.ListToolTips(Handle);
            if (tips != IntPtr.Zero) NativeTheme.SetTheme(tips, theme.NativeThemeName);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A recreated handle loses its theme names.
            ApplyNativeThemes(ThemeManager.Current);
#if DEBUG || QUIETHOSTS_UI_TESTS
            // The native items exist only now (the HandleCreated event comes before ListView inserts them).
            if (PreviewFocusedIndex >= 0 && PreviewFocusedIndex < Items.Count) Items[PreviewFocusedIndex].Focused = true;
#endif
        }

        // ---------------------------------------------------------------- row height

        /// <summary>
        /// Checks the first row's height once items exist and, if a PC ignores the image list, sets the list's font to a Body
        /// font whose height gives <see cref="RowHeight"/> (text is still drawn with the ramp's Body font).
        /// </summary>
        internal void EnsureRowHeight()
        {
            _rowChecked = true;
            if (!IsHandleCreated || Items.Count == 0) return;
            if (GetItemRect(0).Height >= RowHeight - 2) return;
            float points = _ramp.Points(TextStyle.Body) * _ramp.Scale.FontFactor;
            for (int i = 0; i < 160; i++)
            {
                var font = new Font(_ramp.Family, points, FontStyle.Regular, GraphicsUnit.Point);
                Font = font;
                _fallbackFont?.Dispose();
                _fallbackFont = font;
                if (GetItemRect(0).Height >= RowHeight - 1) break;
                points += 0.25f;
            }
            UsesFontFallback = true;
        }

        // ---------------------------------------------------------------- hover and focus

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHot(HitTest(e.Location).Item?.Index ?? -1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHot(-1);
        }

        private void SetHot(int index)
        {
            if (index == _hot) return;
            InvalidateRow(_hot);
            _hot = index;
            InvalidateRow(_hot);
        }

        private void InvalidateRow(int index)
        {
            if (index >= 0 && index < Items.Count) Invalidate(Items[index].Bounds);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        private int HotIndex
        {
            get
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                if (PreviewHotIndex >= 0) return PreviewHotIndex;
#endif
                return _hot;
            }
        }

        private bool RingOnFocusedRow
        {
            get
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                if (PreviewFocusRing) return true;
#endif
                return Focused && (ShowFocusCues || VisualState.CuesForced);
            }
        }

        // ---------------------------------------------------------------- painting

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            Theme t = ThemeManager.Current;
            UiScale s = _ramp.Scale;
            Graphics g = e.Graphics;
            using (var brush = new SolidBrush(t.CardBackground)) g.FillRectangle(brush, e.Bounds);
            int pad = e.ColumnIndex == 0 ? s.Px(16) : s.Px(12);
            var text = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, Math.Max(0, e.Bounds.Width - pad - s.Px(8)), e.Bounds.Height);
            TextRenderer.DrawText(g, e.Header?.Text ?? string.Empty, _ramp.Caption, text, t.TextSecondary,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            int line = s.Stroke(1);
            using (var brush = new SolidBrush(t.Divider)) g.FillRectangle(brush, e.Bounds.X, e.Bounds.Bottom - line, e.Bounds.Width, line);
            e.DrawDefault = false;
        }

        /// <summary>
        /// The list's own custom-draw notification (reflected back to it by WinForms): each row is painted here and the
        /// native list draws nothing of the row (no theme selection, hover or focus rectangle); the empty area below the
        /// rows is filled once the list has painted. The header's notification is a different one (DrawColumnHeader).
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_REFLECT_NOTIFY && m.LParam != IntPtr.Zero && IsHandleCreated)
            {
                var header = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR));
                if (header.code == NM_CUSTOMDRAW && header.hwndFrom == Handle)
                {
                    m.Result = new IntPtr(CustomDraw(m.LParam));
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private int CustomDraw(IntPtr lParam)
        {
            var cd = (NMCUSTOMDRAW)Marshal.PtrToStructure(lParam, typeof(NMCUSTOMDRAW));
            switch (cd.dwDrawStage)
            {
                case CDDS_PREPAINT:
                    return CDRF_NOTIFYITEMDRAW | CDRF_NOTIFYPOSTPAINT;
                case CDDS_ITEMPREPAINT:
                    using (Graphics g = Graphics.FromHdc(cd.hdc)) PaintRow(g, cd.dwItemSpec.ToInt32());
                    return CDRF_SKIPDEFAULT;
                case CDDS_POSTPAINT:
                    using (Graphics g = Graphics.FromHdc(cd.hdc)) PaintBelowRows(g);
                    return CDRF_DODEFAULT;
                default:
                    return CDRF_DODEFAULT;
            }
        }

        /// <summary>One row: the card color, the hover or selection backplate and bar, every cell, the focus ring.</summary>
        private void PaintRow(Graphics g, int index)
        {
            if (index < 0 || index >= Items.Count) return;
            if (!_rowChecked)
            {
                _rowChecked = true;
                BeginInvoke(new Action(EnsureRowHeight));
            }
            ListViewItem item = Items[index];
            Rectangle row = item.Bounds;
            if (row.Width <= 0 || row.Height <= 0) return;
            Theme t = ThemeManager.Current;
            UiScale s = _ramp.Scale;
            using (var brush = new SolidBrush(t.CardBackground)) g.FillRectangle(brush, row);
            bool selected = item.Selected;
            bool hot = index == HotIndex;
            Rectangle plate = Backplate(row);
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            if (selected || hot)
            {
                RoundRect.Prepare(g);
                RoundRect.Fill(g, plate, s.Px(Metrics.Radius), selected ? t.ListSelected : t.ListHover);
                if (selected)
                {
                    int barW = s.Px(Metrics.SelectionBarWidth), barH = s.Px(Metrics.SelectionBarHeight);
                    var bar = new Rectangle(plate.X + s.Px(4), plate.Y + (plate.Height - barH) / 2, barW, barH);
                    RoundRect.Fill(g, bar, s.PxF(1.5f), t.SelectionBar);
                }
                g.SmoothingMode = smoothing;
                g.PixelOffsetMode = offset;
            }
            for (int column = 0; column < Columns.Count; column++)
            {
                Rectangle cell = CellBounds(row, column);
                if (cell.Width <= 0) continue;
                Rectangle content = ContentBounds(cell, column);
                var context = new ListCell(g, item, column, cell, content, selected, hot, t, _ramp);
                Region clip = g.Clip;
                try
                {
                    g.SetClip(cell, CombineMode.Intersect);
                    if (CellPainter != null && CellPainter(context)) continue;
                    string text = column < item.SubItems.Count ? item.SubItems[column].Text : string.Empty;
                    TextRenderer.DrawText(g, text, _ramp.Body, content, context.TextColor,
                        TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
                }
                finally
                {
                    g.Clip = clip;
                    clip.Dispose();
                }
            }
            if (item.Focused && RingOnFocusedRow) FocusRing.Draw(g, plate, t);
        }

        /// <summary>The card color under the last row (the dark list theme draws column lines there).</summary>
        private void PaintBelowRows(Graphics g)
        {
            int top = Items.Count > 0 ? Items[Items.Count - 1].Bounds.Bottom : HeaderBottom();
            Size client = ClientSize;
            if (top >= client.Height) return;
            using (var brush = new SolidBrush(ThemeManager.Current.CardBackground))
                g.FillRectangle(brush, 0, Math.Max(0, top), client.Width, client.Height - Math.Max(0, top));
        }

        /// <summary>The header's bottom edge in client coordinates (0 without a header).</summary>
        private int HeaderBottom()
        {
            IntPtr header = NativeTheme.ListHeader(Handle);
            Rectangle r = NativeTheme.WindowBounds(header);
            return r.IsEmpty ? 0 : PointToClient(new Point(r.Left, r.Bottom)).Y;
        }

        /// <summary>
        /// The header's height in pixels: the native header's once the list has a handle, before that an estimate from the
        /// font (24 px for Body at 96 DPI, as measured).
        /// </summary>
        internal int HeaderHeight
        {
            get
            {
                if (IsHandleCreated && HeaderStyle != ColumnHeaderStyle.None)
                {
                    Rectangle r = NativeTheme.WindowBounds(NativeTheme.ListHeader(Handle));
                    if (!r.IsEmpty) return r.Height;
                }
                return HeaderStyle == ColumnHeaderStyle.None ? 0 : Math.Max(_ramp.Scale.Px(24), Font.Height + _ramp.Scale.Px(5));
            }
        }

        /// <summary>A row's backplate: inset 4 epx left and right, 2 epx top and bottom.</summary>
        private Rectangle Backplate(Rectangle row)
        {
            UiScale s = _ramp.Scale;
            int x = s.Px(4), y = s.Px(2);
            int width = Math.Min(row.Right, ClientSize.Width) - row.X;
            return new Rectangle(row.X + x, row.Y + y, Math.Max(0, width - 2 * x), Math.Max(0, row.Height - 2 * y));
        }

        /// <summary>The cell of <paramref name="column"/> from the row bounds and the column widths (in display order).</summary>
        internal Rectangle CellBounds(Rectangle row, int column)
        {
            int x = row.X;
            for (int i = 0; i < column && i < Columns.Count; i++) x += Columns[i].Width;
            int width = column < Columns.Count ? Columns[column].Width : 0;
            return new Rectangle(x, row.Y, width, row.Height);
        }

        internal Rectangle ContentBounds(Rectangle cell, int column)
        {
            UiScale s = _ramp.Scale;
            int left = column == 0 ? s.Px(16) : s.Px(12);
            return new Rectangle(cell.X + left, cell.Y, Math.Max(0, cell.Width - left - s.Px(8)), cell.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SmallImageList = null;
                _rows?.Dispose();
                _rows = null;
                _fallbackFont?.Dispose();
                _fallbackFont = null;
            }
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMHDR
        {
            public IntPtr hwndFrom;
            public IntPtr idFrom;
            public int code;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMCUSTOMDRAW
        {
            public NMHDR hdr;
            public int dwDrawStage;
            public IntPtr hdc;
            public RECT rc;
            public IntPtr dwItemSpec;
            public int uItemState;
            public IntPtr lItemlParam;
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>The gallery and tests: draw this row as hovered (-1: the real hover).</summary>
        internal int PreviewHotIndex { get; set; } = -1;

        /// <summary>The gallery and tests: draw the focus ring on the focused row even without the focus.</summary>
        internal bool PreviewFocusRing { get; set; }

        /// <summary>The gallery and tests: the row that has the list's focus once the handle exists (-1: none set).</summary>
        internal int PreviewFocusedIndex { get; set; } = -1;

        /// <summary>Tests: no image list, so the font fallback has to give the row height. Set before items are added.</summary>
        internal void PreviewWithoutImageList()
        {
            SmallImageList = null;
            _rows?.Dispose();
            _rows = null;
        }
#endif
    }
}
