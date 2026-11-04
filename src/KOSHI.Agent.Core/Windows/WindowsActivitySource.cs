using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace KOSHI.Agent.Core.Windows;

public interface IActivitySource
{
    ForegroundInfo? GetForeground();
    double GetIdleSeconds();
}

/// <summary>
/// Reads the foreground window and idle time through documented Win32 APIs. Uses only
/// PROCESS_QUERY_LIMITED_INFORMATION; processes the agent cannot query are reported as "unknown.exe".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsActivitySource : IActivitySource
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public ForegroundInfo? GetForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        var title = ReadTitle(hwnd);
        var name = ProcessImageName(pid) ?? "unknown.exe";
        if (string.Equals(name, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var coreChild = FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (coreChild != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(coreChild, out var childPid);
                if (childPid != 0 && ProcessImageName(childPid) is { } childName)
                {
                    pid = childPid;
                    name = childName;
                }
            }
        }
        return new ForegroundInfo(name.ToLowerInvariant(), title, (int)pid);
    }

    public double GetIdleSeconds()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return idleMs / 1000.0;
    }

    public static string? ProcessImageName(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? ReadTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0) return null;
        var sb = new StringBuilder(Math.Min(len + 1, 1024));
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
