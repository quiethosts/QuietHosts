using System;
using System.Collections.Generic;
using System.IO;

namespace QuietHosts.Core
{
    /// <summary>One address from the shipped lists.</summary>
    public sealed class ListEntry
    {
        public ListEntry(string host, Tier tier, string category, string company, string note, int line)
        {
            Host = host;
            Tier = tier;
            Category = category;
            Company = company;
            Note = note;
            Line = line;
        }

        /// <summary>The normalized address, for example "pagead2.googlesyndication.com".</summary>
        public string Host { get; }
        public Tier Tier { get; }
        /// <summary>A known category key (<see cref="Categories.Keys"/>); unknown keys load as "other".</summary>
        public string Category { get; }
        /// <summary>The UI words for the category ("What it is" column).</summary>
        public string CategoryWords => Categories.Words(Category);
        /// <summary>The company, or "" if none is given.</summary>
        public string Company { get; }
        /// <summary>The known effects of blocking it, or "" if none are known.</summary>
        public string Note { get; }
        /// <summary>1-based line number in its list file.</summary>
        public int Line { get; }

        public override string ToString() => Host;
    }

    public enum ListIssueKind
    {
        // Errors: the line (or file) is not loaded.
        InvalidHost,
        InvalidField,
        FieldTooLong,
        TooManyFields,
        Duplicate,
        InvalidCategoryLine,
        InvalidVersion,
        FileTooLarge,
        InvalidUtf8,
        TooManyHosts,
        MissingFile,
        // Lint problems: the line still loads, but the shipped lists must not have them.
        UnknownCategory,
        NoCategory,
        MissingNote,
        MissingVersion,
        VersionMismatch,
        ByteOrderMark,
        CarriageReturn,
    }

    /// <summary>A problem found while parsing a list file. Line is 0 for file-level problems.</summary>
    public sealed class ListIssue
    {
        public ListIssue(string file, int line, ListIssueKind kind, string message)
        {
            File = file;
            Line = line;
            Kind = kind;
            Message = message;
        }

        public string File { get; }
        public int Line { get; }
        public ListIssueKind Kind { get; }
        public string Message { get; }

        /// <summary>True when the line or file was not loaded because of this problem.</summary>
        public bool IsError => Kind <= ListIssueKind.MissingFile;

        public override string ToString() => Line > 0 ? $"{File}:{Inv.Num(Line)}: {Kind}: {Message}" : $"{File}: {Kind}: {Message}";
    }

    /// <summary>Category keys and their UI words ("What it is" in the Fix dialog).</summary>
    public static class Categories
    {
        public const string Other = "other";

        private static readonly string[] KeyOrder =
        {
            "tag-manager", "google-ads", "amazon-ads", "social-pixel", "ad-server", "ad-exchange",
            "header-bidding", "native-ads", "video-ads", "popunder-redirect", "push-ads", "anti-adblock",
            "cookie-sync-identity", "ad-verification", "affiliate", "data-broker", "analytics",
            "session-recording", "app-ads", "cryptominer", "fingerprinting", "other",
        };

        private static readonly Dictionary<string, string> WordsByKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tag-manager"] = "Tag manager",
            ["google-ads"] = "Google ads",
            ["amazon-ads"] = "Amazon ads",
            ["social-pixel"] = "Social network ad pixel",
            ["ad-server"] = "Ad server",
            ["ad-exchange"] = "Ad exchange",
            ["header-bidding"] = "Ad auction",
            ["native-ads"] = "\"Around the web\" ad box",
            ["video-ads"] = "Video ads",
            ["popunder-redirect"] = "Pop-under or redirect ads",
            ["push-ads"] = "Push-notification ads",
            ["anti-adblock"] = "Ad-blocker detection",
            ["cookie-sync-identity"] = "Cross-site tracking",
            ["ad-verification"] = "Ad verification",
            ["affiliate"] = "Affiliate tracking",
            ["data-broker"] = "Data broker",
            ["analytics"] = "Analytics tracker",
            ["session-recording"] = "Session recording",
            ["app-ads"] = "App ads and attribution",
            ["cryptominer"] = "Cryptominer",
            ["fingerprinting"] = "Fingerprinting",
            ["other"] = "Ads or tracking",
        };

        /// <summary>Every known key, in the order of the table in CONTRIBUTING.md.</summary>
        public static IReadOnlyList<string> Keys => KeyOrder;

        public static bool IsKnown(string? key) => key != null && WordsByKey.ContainsKey(key);

        /// <summary>The UI words for a key; unknown keys get the words for "other" ("Ads or tracking").</summary>
        public static string Words(string? key) =>
            key != null && WordsByKey.TryGetValue(key, out string? words) ? words : WordsByKey[Other];
    }

    internal static class EmbeddedLists
    {
        public const string DefaultName = "default.txt";
        public const string StrictName = "strict.txt";
        public const string GuardName = "guard.txt";
        private const string Prefix = "QuietHosts.Lists.";

        /// <summary>The bytes of an embedded list (at most the list size limit plus one), or null if it is missing.</summary>
        public static byte[]? Read(string fileName)
        {
            using (Stream? stream = typeof(EmbeddedLists).Assembly.GetManifestResourceStream(Prefix + fileName))
            {
                if (stream == null) return null;
                return ReadCapped(stream, Limits.MaxListFileBytes + 1);
            }
        }

        internal static byte[] ReadCapped(Stream stream, int max)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[16384];
                int total = 0;
                while (total < max)
                {
                    int n = stream.Read(buffer, 0, Math.Min(buffer.Length, max - total));
                    if (n <= 0) break;
                    ms.Write(buffer, 0, n);
                    total += n;
                }
                return ms.ToArray();
            }
        }
    }
}
