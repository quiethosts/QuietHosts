using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace QuietHosts.Core
{
    /// <summary>Fixed limits. Every count shown to users is computed from the lists, never from here.</summary>
    public static class Limits
    {
        /// <summary>Hosts files over this size are refused (TooLarge).</summary>
        public const long MaxHostsFileBytes = 16L * 1024 * 1024;
        /// <summary>LargeFile warning when the bytes outside the managed blocks are over this.</summary>
        public const long LargeFileBytes = 256L * 1024;
        public const int MaxListFileBytes = 256 * 1024;
        public const int MaxListHosts = 3000;
        public const int MaxCompanyLength = 60;
        public const int MaxNoteLength = 80;
        public const int MaxAllowlist = 2000;
        /// <summary>At most this many addresses per Allow or Block run.</summary>
        public const int MaxHostsPerRun = 100;
        public const int MaxLastRunChars = 8000;
        /// <summary>Timestamped backups kept next to the hosts file.</summary>
        public const int BackupsKept = 10;
    }

    /// <summary>Latin-1 (code page 28591): each byte is one char and back, so every byte round-trips.</summary>
    public static class Latin1
    {
        public static string Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
            return new string(chars);
        }

        /// <summary>Encodes text that came from <see cref="Decode"/> or is ASCII. Throws for chars above U+00FF.</summary>
        public static byte[] Encode(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c > (char)0xFF) throw new ArgumentException("Text contains a character that Latin-1 can't encode.", nameof(text));
                bytes[i] = (byte)c;
            }
            return bytes;
        }
    }

    /// <summary>SHA-256 helpers. Hex is always lowercase.</summary>
    public static class Hashing
    {
        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        public static string Sha256Hex(string asciiText) => Sha256Hex(Encoding.UTF8.GetBytes(asciiText));

        public static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(digits[b >> 4]);
                sb.Append(digits[b & 0xF]);
            }
            return sb.ToString();
        }

        public static bool IsSha256Hex(string? text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        public static bool BytesEqual(byte[]? a, byte[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }

    /// <summary>Culture-free formatting, so the current culture never changes our text.</summary>
    internal static class Inv
    {
        public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>ASCII-only lowercase; other chars unchanged. Used for names that may not be valid hosts.</summary>
        public static string AsciiLower(string text)
        {
            char[]? chars = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z')
                {
                    chars ??= text.ToCharArray();
                    chars[i] = (char)(c + 32);
                }
            }
            return chars == null ? text : new string(chars);
        }
    }
}
