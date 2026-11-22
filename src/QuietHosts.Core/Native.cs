using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QuietHosts.Core
{
    /// <summary>P/Invokes. The assembly attribute DefaultDllImportSearchPaths(System32) applies to all of them.</summary>
    internal static class Native
    {
        public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        public const ushort DNS_TYPE_A = 0x0001;
        public const uint DNS_QUERY_NO_WIRE_QUERY = 0x00000010;
        public const int DnsFreeRecordList = 1;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_ALL = 0x00000007;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr results, IntPtr reserved);

        [DllImport("dnsapi.dll", ExactSpelling = true)]
        public static extern void DnsRecordListFree(IntPtr recordList, int freeType);

        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

        /// <summary>The Downloads known folder of the current user, or null.</summary>
        public static string? DownloadsFolder()
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(downloads, 0, IntPtr.Zero, out p) != 0) return null;
                return Marshal.PtrToStringUni(p);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return null;
            }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
            }
        }

        /// <summary>The path with junctions and symbolic links resolved, or null if it doesn't exist or can't be opened.</summary>
        public static string? FinalPath(string path)
        {
            try
            {
                using (SafeFileHandle h = CreateFile(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    var sb = new StringBuilder(1024);
                    uint n = GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
                    if (n == 0) return null;
                    if (n >= sb.Capacity)
                    {
                        sb = new StringBuilder((int)n + 1);
                        n = GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
                        if (n == 0 || n >= sb.Capacity) return null;
                    }
                    return StripDevicePrefix(sb.ToString());
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is IOException)
            {
                return null;
            }
        }

        internal static string StripDevicePrefix(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }
    }
}
