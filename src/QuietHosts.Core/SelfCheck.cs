using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace QuietHosts.Core
{
    /// <summary>The self-check's outcome. Anything but Passed shows SelfCheckFailed.</summary>
    public sealed class SelfCheckResult
    {
        public SelfCheckResult(bool passed, string detail)
        {
            Passed = passed;
            Detail = detail;
        }

        public bool Passed { get; }
        /// <summary>What happened, for Copy details.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// After a successful Turn on: looks up the first written address in the DNS Client's local
    /// cache, which holds the hosts entries, with DNS_QUERY_NO_WIRE_QUERY, so no query ever goes on the network.
    /// Passed means an A record with address 0.0.0.0. In the sandbox (and tests) it reads that hosts file instead.
    /// </summary>
    public static class SelfCheck
    {
        public static Task<SelfCheckResult> CheckAsync(AppEnvironment env, string host) => Task.Run(() => Check(env, host));

        public static SelfCheckResult Check(AppEnvironment env, string host)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            if (!HostName.TryNormalize(host, out string name)) return new SelfCheckResult(false, "not a valid address");
            return env.IsReal ? QueryDnsCache(name) : CheckHostsFile(env, name);
        }

        /// <summary>DnsQuery_W(name, DNS_TYPE_A, DNS_QUERY_NO_WIRE_QUERY): the local cache only.</summary>
        internal static SelfCheckResult QueryDnsCache(string name)
        {
            IntPtr results = IntPtr.Zero;
            try
            {
                int status = Native.DnsQuery(name, Native.DNS_TYPE_A, Native.DNS_QUERY_NO_WIRE_QUERY, IntPtr.Zero, out results, IntPtr.Zero);
                if (status != 0) return new SelfCheckResult(false, "DnsQuery status " + Inv.Num(status));
                int typeOffset = 2 * IntPtr.Size;
                int dataOffset = 2 * IntPtr.Size + 16;
                for (IntPtr rec = results; rec != IntPtr.Zero; rec = Marshal.ReadIntPtr(rec, 0))
                {
                    ushort type = (ushort)Marshal.ReadInt16(rec, typeOffset);
                    if (type != Native.DNS_TYPE_A) continue;
                    uint address = (uint)Marshal.ReadInt32(rec, dataOffset);
                    if (address == 0) return new SelfCheckResult(true, "0.0.0.0 from the DNS cache");
                    return new SelfCheckResult(false, "the DNS cache has another address");
                }
                return new SelfCheckResult(false, "no A record in the DNS cache");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return new SelfCheckResult(false, ex.GetType().Name);
            }
            finally
            {
                if (results != IntPtr.Zero) Native.DnsRecordListFree(results, Native.DnsFreeRecordList);
            }
        }

        /// <summary>The sandbox variant: the hosts file's QuietHosts block contains "0.0.0.0 name".</summary>
        internal static SelfCheckResult CheckHostsFile(AppEnvironment env, string name)
        {
            try
            {
                byte[]? bytes = env.Files.ReadFile(env.Location.HostsPath, Limits.MaxHostsFileBytes);
                HostsDocument doc = HostsDocument.Parse(bytes);
                foreach (string h in doc.BlockHosts)
                {
                    if (string.Equals(h, name, StringComparison.Ordinal)) return new SelfCheckResult(true, "0.0.0.0 in the hosts file");
                }
                return new SelfCheckResult(false, "not in the hosts file's block");
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                return new SelfCheckResult(false, ex.GetType().Name);
            }
        }
    }
}
