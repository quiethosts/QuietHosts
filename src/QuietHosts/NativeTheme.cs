using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// The Windows calls behind the themes, glyphs and icons (spec 2.5, 2.8.9, 2.8.10). Every call is wrapped: a failure
    /// leaves that part in its light look; it never throws and never changes layout. No comctl32 entry points (the test
    /// host has only comctl32 5.82).
    /// </summary>
    internal static class NativeTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
            SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;
        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
        public const int LVM_GETHEADER = 0x101F;
        public const int LVM_GETTOOLTIPS = 0x104E;
        public const int EM_GETRECT = 0x00B2;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_SYSCOLORCHANGE = 0x0015;
        public const int WM_THEMECHANGED = 0x031A;
        public const int WM_PAINT = 0x000F;
        public const int WM_PRINTCLIENT = 0x0318;
        public const uint GGI_MARK_NONEXISTING_GLYPHS = 0x0001;
        public const uint SIID_SHIELD = 77;
        public const uint SHGSI_ICONLOCATION = 0x0, SHGSI_ICON = 0x100, SHGSI_SMALLICON = 0x1;

        private static int? _osBuild;

        private static bool IsNativeFailure(Exception ex) =>
            ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is COMException || ex is Win32Exception;

        // ---------------------------------------------------------------- Windows version

        /// <summary>The Windows build number from RtlGetVersion (not Environment.OSVersion, which a host without the app manifest can misreport).</summary>
        public static int OsBuild => _osBuild ??= ReadOsBuild();

        private static int ReadOsBuild()
        {
            try
            {
                var info = new OSVERSIONINFOEXW { dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEXW)) };
                if (RtlGetVersion(ref info) == 0 && info.dwBuildNumber > 0) return info.dwBuildNumber;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
            }
            return Environment.OSVersion.Version.Build;
        }

        public static bool IsWindows11 => OsBuild >= ThemeResolver.FirstWindows11Build;

        // ---------------------------------------------------------------- title bar and native parts

        /// <summary>
        /// Sets DWM attribute 20 (dark title bar and frame) on Windows 11; never called on Windows 10. Returns whether it was
        /// set. When <paramref name="repaintNow"/>, the frame repaints at once (not at the next activation).
        /// </summary>
        public static bool ApplyFrame(IntPtr hwnd, bool dark, bool repaintNow)
        {
            if (hwnd == IntPtr.Zero || !IsWindows11) return false;
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                if (repaintNow) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
                return true;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return false;
            }
        }

        /// <summary>SetWindowTheme(hwnd, name, null); null restores the default look. Failures are ignored.</summary>
        public static void SetTheme(IntPtr hwnd, string? name)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                SetWindowTheme(hwnd, name, null);
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
            }
        }

        /// <summary>Windows' "Animation effects" setting; true when it can't be read.</summary>
        public static bool ClientAreaAnimation()
        {
            try
            {
                bool on = true;
                return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) || on;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return true;
            }
        }

        /// <summary>Invalidates a native child window (the list header) so it repaints in the new theme.</summary>
        public static void Repaint(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try { InvalidateRect(hwnd, IntPtr.Zero, true); }
            catch (Exception ex) when (IsNativeFailure(ex)) { }
        }

        public static IntPtr ListHeader(IntPtr list) => SafeSend(list, LVM_GETHEADER);
        public static IntPtr ListToolTips(IntPtr list) => SafeSend(list, LVM_GETTOOLTIPS);

        /// <summary>A window's bounds in screen coordinates (GetWindowRect), or empty.</summary>
        public static Rectangle WindowBounds(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Rectangle.Empty;
            try
            {
                return GetWindowRect(hwnd, out RECT r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return Rectangle.Empty;
            }
        }

        private static IntPtr SafeSend(IntPtr hwnd, int msg)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                return SendMessage(hwnd, msg, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>An edit control's formatting rectangle (where typed text starts), or the client rectangle.</summary>
        public static Rectangle EditRect(TextBox box)
        {
            try
            {
                var r = new RECT();
                SendMessage(box.Handle, EM_GETRECT, IntPtr.Zero, ref r);
                if (r.Right > r.Left && r.Bottom > r.Top) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
            }
            return box.ClientRectangle;
        }

        /// <summary>A multi-line edit control's number of lines as it wraps them (EM_GETLINECOUNT), or 0.</summary>
        public static int EditLineCount(TextBox box)
        {
            if (box == null || !box.IsHandleCreated) return 0;
            return (int)SafeSend(box.Handle, 0x00BA).ToInt64();
        }

        public static bool TryHideCaret(IntPtr hwnd)
        {
            try { return HideCaret(hwnd); }
            catch (Exception ex) when (IsNativeFailure(ex)) { return false; }
        }

        public static void ShowCaretAgain(IntPtr hwnd)
        {
            try { ShowCaret(hwnd); }
            catch (Exception ex) when (IsNativeFailure(ex)) { }
        }

        /// <summary>The string a WM_SETTINGCHANGE carries in lParam ("ImmersiveColorSet" for a light/dark switch), or null.</summary>
        public static string? SettingName(IntPtr lParam)
        {
            if (lParam == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(lParam);
            }
            catch (Exception ex) when (ex is AccessViolationException || ex is ArgumentException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- glyph coverage

        /// <summary>True if <paramref name="font"/> has a glyph for <paramref name="c"/> (GetGlyphIndicesW).</summary>
        public static bool FontHasGlyph(Font font, char c)
        {
            IntPtr dc = IntPtr.Zero, hfont = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                hfont = font.ToHfont();
                old = SelectObject(dc, hfont);
                var indices = new ushort[1];
                uint n = GetGlyphIndicesW(dc, c.ToString(), 1, indices, GGI_MARK_NONEXISTING_GLYPHS);
                return n == 1 && indices[0] != 0xFFFF;
            }
            catch (Exception ex) when (IsNativeFailure(ex) || ex is ArgumentException)
            {
                return false;
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(dc, old);
                if (hfont != IntPtr.Zero) DeleteObject(hfont);
                if (dc != IntPtr.Zero) DeleteDC(dc);
            }
        }

        // ---------------------------------------------------------------- icons

        /// <summary>The stock UAC shield's icon file and index (SHGetStockIconInfo with SHGSI_ICONLOCATION), or null.</summary>
        public static (string Path, int Index)? ShieldLocation()
        {
            try
            {
                var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf(typeof(SHSTOCKICONINFO)) };
                if (SHGetStockIconInfo(SIID_SHIELD, SHGSI_ICONLOCATION, ref info) != 0 || string.IsNullOrEmpty(info.szPath)) return null;
                return (info.szPath, info.iIcon);
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return null;
            }
        }

        /// <summary>The small stock shield icon (SM_CXSMICON), or zero. The caller destroys it.</summary>
        public static IntPtr SmallShieldIcon()
        {
            try
            {
                var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf(typeof(SHSTOCKICONINFO)) };
                return SHGetStockIconInfo(SIID_SHIELD, SHGSI_ICON | SHGSI_SMALLICON, ref info) == 0 ? info.hIcon : IntPtr.Zero;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>An icon from a file at exactly <paramref name="px"/> pixels (SHDefExtractIconW), or zero. The caller destroys it.</summary>
        public static IntPtr ExtractIcon(string path, int index, int px)
        {
            if (string.IsNullOrEmpty(path) || px <= 0) return IntPtr.Zero;
            try
            {
                return SHDefExtractIcon(path, index, 0, out IntPtr large, IntPtr.Zero, (uint)px) == 0 ? large : IntPtr.Zero;
            }
            catch (Exception ex) when (IsNativeFailure(ex))
            {
                return IntPtr.Zero;
            }
        }

        public static void Destroy(IntPtr icon)
        {
            if (icon == IntPtr.Zero) return;
            try { DestroyIcon(icon); }
            catch (Exception ex) when (IsNativeFailure(ex)) { }
        }

#if DEBUG
        /// <summary>Debug --render-ui: the window as the screen shows it (PW_RENDERFULLCONTENT), including a dark title bar.</summary>
        public static bool TryPrintWindow(IntPtr hwnd, IntPtr hdc)
        {
            try { return PrintWindow(hwnd, hdc, 2); }
            catch (Exception ex) when (IsNativeFailure(ex)) { return false; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
#endif

        // ---------------------------------------------------------------- P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEXW
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysImageIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("ntdll.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEXW info);

        [DllImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] ref bool value, uint winIni);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref RECT lParam);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HideCaret(IntPtr hwnd);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowCaret(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetGlyphIndicesW(IntPtr hdc, string text, int count, [Out] ushort[] indices, uint flags);

        [DllImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SHGetStockIconInfo(uint siid, uint flags, ref SHSTOCKICONINFO info);

        [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, IntPtr small, uint size);
    }
}
