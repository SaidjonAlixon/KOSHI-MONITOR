using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KOSHI.Agent.Service;

/// <summary>Makes web-filter changes take effect in running browsers without a restart.</summary>
[SupportedOSPlatform("windows")]
internal static partial class BrowserNet
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "browser", "vivaldi", "chromium", "iexplore",
    };

    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint StateEstablished = 5;
    private const uint StateDeleteTcb = 12;
    private const uint RpForce = 1;

    /// <summary>Signals Group Policy refresh so Chrome/Edge reload URLBlocklist from the registry within seconds.</summary>
    public static void RefreshPolicies()
    {
        RefreshPolicyEx(true, RpForce);
        RefreshPolicyEx(false, RpForce);
    }

    /// <summary>
    /// Drops browsers' open IPv4 web connections so tabs already showing a newly blocked site must reconnect
    /// (and hit the hosts / policy block). Other sites simply reconnect.
    /// </summary>
    public static void ResetBrowserConnections(ILogger logger)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != 0 && Browsers.Contains(p.ProcessName)) pids.Add((uint)p.Id);
            }
            catch
            {
                // Process exited.
            }
            finally
            {
                p.Dispose();
            }
        }
        if (pids.Count == 0) return;

        var size = 0;
        _ = GetExtendedTcpTable(0, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        if (size <= 0) return;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0) return;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            var reset = 0;
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + i * rowSize);
                if (row.State != StateEstablished || !pids.Contains(row.OwningPid)) continue;
                var port = (ushort)(((row.RemotePort & 0xFF) << 8) | ((row.RemotePort >> 8) & 0xFF));
                if (port is not (443 or 80)) continue;
                var entry = new TcpRow
                {
                    State = StateDeleteTcb,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort,
                };
                if (SetTcpEntry(ref entry) == 0) reset++;
            }
            logger.LogInformation("Web filter: reset {Count} browser connections", reset);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RefreshPolicyEx([MarshalAs(UnmanagedType.Bool)] bool machine, uint options);

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(nint table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int af, int tableClass, uint reserved);

    [LibraryImport("iphlpapi.dll")]
    private static partial uint SetTcpEntry(ref TcpRow row);
}
