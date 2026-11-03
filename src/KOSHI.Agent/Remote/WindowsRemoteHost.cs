using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using KOSHI.Agent.Core.Remote;
using KOSHI.Contracts;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace KOSHI.Agent.Remote;

public sealed partial class WindowsRemoteHost(Func<Task> refreshPolicy) : IRemoteHost
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "winlogon", "csrss", "lsass", "services", "svchost", "smss", "wininit", "dwm", "logonui",
        "koshi.agent", "koshi.agent.service", "msmpeng", "securityhealthservice",
    };

    private readonly ImageCodecInfo _jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    private readonly Dictionary<Guid, RemoteBanner> _banners = [];

    public void ShowSessionBanner(Guid sessionId, string adminName, Action endByEmployee) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            var banner = new RemoteBanner(adminName, endByEmployee);
            _banners[sessionId] = banner;
            banner.Show();
        });

    public void HideSessionBanner(Guid sessionId) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_banners.Remove(sessionId, out var b)) b.Close();
        });

    public (byte[] Jpeg, int Width, int Height)? CaptureFrame(int maxWidth, int quality)
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return null;
        var bounds = screen.Bounds;
        try
        {
            using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            var scale = Math.Min(1.0, (double)maxWidth / bounds.Width);
            var w = (int)(bounds.Width * scale);
            var h = (int)(bounds.Height * scale);
            using var scaled = scale < 1 ? new Bitmap(w, h, PixelFormat.Format24bppRgb) : null;
            if (scaled is not null)
            {
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(full, 0, 0, w, h);
            }

            using var ms = new MemoryStream();
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 20, 90));
            (scaled ?? full).Save(ms, _jpeg, p);
            return (ms.ToArray(), w, h);
        }
        catch (Exception)
        {
            // Secure desktop (UAC prompt, lock screen) cannot be captured from the user session.
            return null;
        }
    }

    public void InjectInput(RemoteInputDto input)
    {
        switch (input.Type)
        {
            case "move":
                Mouse(input.X, input.Y, MouseMove | MouseAbsolute, 0);
                break;
            case "down":
            case "up":
                var down = input.Type == "down";
                var flag = input.Button switch
                {
                    1 => down ? MouseMiddleDown : MouseMiddleUp,
                    2 => down ? MouseRightDown : MouseRightUp,
                    _ => down ? MouseLeftDown : MouseLeftUp,
                };
                Mouse(input.X, input.Y, MouseMove | MouseAbsolute | flag, 0);
                break;
            case "wheel":
                Mouse(input.X, input.Y, MouseWheel, -Math.Sign(input.Delta) * 120);
                break;
            case "keydown":
            case "keyup":
                if (input.KeyCode is > 0 and < 255) Key((ushort)input.KeyCode, input.Type == "keyup");
                break;
        }
    }

    public async Task<(bool Success, string Message)> ExecuteAsync(RemoteCommandDto c)
    {
        var note = $"Administrator {c.RequestedBy} so'rovi (KOSHI)";
        switch (c.Command)
        {
            case RemoteCommands.Lock:
                return LockWorkStation() ? (true, "Kompyuter bloklandi") : (false, "Bloklab bo'lmadi");
            case RemoteCommands.Message:
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    MessageBox.Show(c.Argument ?? string.Empty, $"KOSHI — {c.RequestedBy} xabari", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information));
                return (true, "Xabar ko'rsatildi");
            case RemoteCommands.Logoff:
                return RunShutdown("/l");
            case RemoteCommands.Restart:
                return RunShutdown($"/r /t 60 /c \"{note}: kompyuter 60 soniyadan keyin qayta ishga tushadi\"");
            case RemoteCommands.Shutdown:
                return RunShutdown($"/s /t 60 /c \"{note}: kompyuter 60 soniyadan keyin o'chadi\"");
            case RemoteCommands.CancelShutdown:
                return RunShutdown("/a");
            case RemoteCommands.KillProcess:
                return KillProcess(c.Argument);
            case RemoteCommands.RefreshPolicy:
                await refreshPolicy();
                return (true, "Siyosat yangilandi");
            default:
                return (false, "Qo'llab-quvvatlanmaydigan buyruq");
        }
    }

    private static (bool, string) KillProcess(string? argument)
    {
        var name = Path.GetFileNameWithoutExtension(argument ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name)) return (false, "Jarayon nomi ko'rsatilmagan");
        if (ProtectedProcesses.Contains(name)) return (false, "Tizim jarayonini yopish taqiqlangan");
        var session = Process.GetCurrentProcess().SessionId;
        var killed = 0;
        foreach (var p in Process.GetProcessesByName(name).Where(p => p.SessionId == session))
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(3000)) p.Kill();
                killed++;
            }
            catch (Exception)
            {
                // Access denied (elevated process) — reported via the count below.
            }
            finally
            {
                p.Dispose();
            }
        }
        return killed > 0 ? (true, $"{killed} ta jarayon yopildi") : (false, "Jarayon topilmadi yoki ruxsat yo'q");
    }

    private static (bool, string) RunShutdown(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0 ? (true, "Buyruq bajarildi") : (false, $"shutdown.exe kodi {p?.ExitCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ---------------- Win32 input ----------------

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseRightDown = 0x0008, MouseRightUp = 0x0010,
        MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040, MouseWheel = 0x0800, MouseAbsolute = 0x8000;
    private const uint KeyUp = 0x0002, KeyExtended = 0x0001;
    private static readonly HashSet<ushort> ExtendedKeys = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90];

    private static void Mouse(double x, double y, uint flags, int data)
    {
        var input = new Input
        {
            Type = InputMouse,
            U = new InputUnion { Mi = new MouseInput { Dx = (int)(x * 65535), Dy = (int)(y * 65535), MouseData = data, Flags = flags } },
        };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void Key(ushort vk, bool up)
    {
        var flags = (up ? KeyUp : 0) | (ExtendedKeys.Contains(vk) ? KeyExtended : 0);
        var input = new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Vk = vk, Flags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeybdInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
        private readonly uint _pad1;
        private readonly uint _pad2;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
