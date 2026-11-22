using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace QuietHosts
{
    /// <summary>Whether this QuietHosts.exe carries a digital signature, as read offline.</summary>
    internal enum SignatureKind
    {
        /// <summary>No signature at all (every v1 build).</summary>
        Unsigned,
        /// <summary>Windows verified the signature, from the local certificate stores only.</summary>
        Signed,
        /// <summary>A signature is there, but Windows couldn't confirm it.</summary>
        Unverified,
    }

    /// <summary>A plain class, not a record: the test assembly compiles this file too.</summary>
    internal sealed class SignatureState
    {
        public SignatureState(SignatureKind kind, string? publisher)
        {
            Kind = kind;
            Publisher = publisher;
        }

        public SignatureKind Kind { get; }
        /// <summary>The signer's simple name (Signed only), without control characters, at most 64 characters.</summary>
        public string? Publisher { get; }
    }

    /// <summary>
    /// Reads whether an exe is signed, offline (spec 2.8.18). First the PE header's security directory: size 0 means
    /// Unsigned, a plain file read that can't touch the network (every v1 build ends here). Only when a signature blob
    /// exists: WinVerifyTrust with cache-only URL retrieval and no revocation checks, so only the local certificate stores
    /// are used.
    /// </summary>
    internal static class SignatureCheck
    {
        private const int HeaderBytes = 4096;
        private const int PublisherMax = 64;

        public static SignatureState Read(string exePath)
        {
            bool hasBlob;
            try
            {
                hasBlob = HasSignatureBlob(exePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException ||
                ex is System.Security.SecurityException)
            {
                // A read error before a blob was found.
                return new SignatureState(SignatureKind.Unsigned, null);
            }
            if (!hasBlob) return new SignatureState(SignatureKind.Unsigned, null);
            try
            {
                if (Verify(exePath) != 0) return new SignatureState(SignatureKind.Unverified, null);
                return new SignatureState(SignatureKind.Signed, Publisher(exePath));
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return new SignatureState(SignatureKind.Unverified, null);
            }
        }

        /// <summary>
        /// True if the PE file has a non-empty security data directory (an Authenticode blob). False for an unsigned PE and
        /// for anything that isn't a well-formed PE file.
        /// </summary>
        internal static bool HasSignatureBlob(string path)
        {
            var header = new byte[HeaderBytes];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                read = 0;
                while (read < header.Length)
                {
                    int n = stream.Read(header, read, header.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            if (read < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z') return false;
            int peOffset = BitConverter.ToInt32(header, 0x3C);
            if (peOffset < 0x40 || peOffset > read - 4 - 20 - 2) return false;
            if (header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E' || header[peOffset + 2] != 0 || header[peOffset + 3] != 0) return false;
            int optional = peOffset + 4 + 20;
            ushort magic = BitConverter.ToUInt16(header, optional);
            int countOffset, directories;
            if (magic == 0x10B) { countOffset = optional + 92; directories = optional + 96; }
            else if (magic == 0x20B) { countOffset = optional + 108; directories = optional + 112; }
            else return false;
            if (countOffset + 4 > read) return false;
            uint count = BitConverter.ToUInt32(header, countOffset);
            // IMAGE_DIRECTORY_ENTRY_SECURITY is entry 4, 8 bytes per entry: the address, then the size.
            const int SecurityEntry = 4;
            if (count <= SecurityEntry) return false;
            int sizeOffset = directories + SecurityEntry * 8 + 4;
            if (sizeOffset + 4 > read) return false;
            return BitConverter.ToUInt32(header, sizeOffset) != 0;
        }

        private static int Verify(string path)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            VerifyCalls++;
#endif
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };
            IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, fileInfo, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfo,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE,
                };
                Guid action = WintrustActionGenericVerifyV2;
                int result = WinVerifyTrust(NoUi, ref action, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(NoUi, ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.DestroyStructure(fileInfo, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(fileInfo);
            }
        }

        /// <summary>The signer certificate's simple name, without control characters, at most 64 characters.</summary>
        private static string? Publisher(string path)
        {
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    string name = cert.GetNameInfo(X509NameType.SimpleName, false) ?? string.Empty;
                    var sb = new StringBuilder(name.Length);
                    foreach (char c in name)
                    {
                        if (!char.IsControl(c)) sb.Append(c);
                    }
                    string clean = sb.ToString().Trim();
                    if (clean.Length > PublisherMax) clean = clean.Substring(0, PublisherMax);
                    return clean.Length > 0 ? clean : null;
                }
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: how many verifications (WinVerifyTrust with WTD_STATEACTION_VERIFY) ran.</summary>
        internal static int VerifyCalls { get; set; }
#endif

        // ---------------------------------------------------------------- WinVerifyTrust

        private static readonly IntPtr NoUi = new IntPtr(-1);
        private static readonly Guid WintrustActionGenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        /// <summary>WINTRUST_DATA including the Windows 8+ pSignatureSettings field (cbStruct is its full size).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    }
}
