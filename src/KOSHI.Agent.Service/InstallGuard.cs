using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KOSHI.Agent.Core;
using KOSHI.Contracts;

namespace KOSHI.Agent.Service;

/// <summary>
/// Software-installation lock. Every installer started in a user session — normally or with "Run as administrator",
/// setup EXE, MSI, winget, App Installer or Microsoft Store — is frozen (NtSuspendProcess) the moment it starts.
/// The tray agent asks for the protection password; only the service verifies it. Correct password resumes that
/// installer (and its child processes) only — the next setup asks again. Cancel, wrong password (3x) or no answer
/// in 3 minutes kills that installer. Uninstallers and updates of already installed programs (Program Files) are not affected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class InstallGuard(PolicyState state, IHostApplicationLifetime lifetime, ILogger<InstallGuard> logger) : BackgroundService
{
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(3);
    private const int MaxAttempts = 3;

    private static readonly HashSet<string> InstallerHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec", "winget", "appinstaller", "appinstallercli", "winstore.app", "microsoft.desktopappinstaller",
        "desktopappinstaller", "storeexperiencehost", "wsreset", "installmanagerapp", "windowspackageinstaller",
        "msixmgr", "appxdeploymentclient",
    };
    private static readonly string[] NameKeywords =
        ["setup", "instal", "stub", "nsis", "inno", "tsetup", "bootstrap", "unpack", "sfx"];
    private static readonly string[] MetaMarks =
    [
        "setup", "installer", "install ", "inno setup", "nullsoft", "nsis", "installshield", "wix toolset", "advanced installer",
        "installaware", "setup factory", "wise installation", "install4j", "smart install maker", "actual installer", "installforge",
        "squirrel", "clickonce", "bootstrapper", "web installer", "offline installer", "msix", "appx bundle",
    ];
    private static readonly byte[][] BinaryMarks =
    [
        Encoding.ASCII.GetBytes("Inno Setup"), Encoding.ASCII.GetBytes("Nullsoft.NSIS"), Encoding.ASCII.GetBytes("NullsoftInst"),
        Encoding.ASCII.GetBytes("InstallShield"), Encoding.ASCII.GetBytes(".wixburn"), Encoding.ASCII.GetBytes("WixBundle"),
        Encoding.ASCII.GetBytes("Advanced Installer"), Encoding.ASCII.GetBytes("InstallAware"), Encoding.ASCII.GetBytes("install4j"),
        Encoding.ASCII.GetBytes("Squirrel"), Encoding.ASCII.GetBytes("!SFX!"), Encoding.ASCII.GetBytes("7-Zip"),
    ];

    private readonly Lock _gate = new();
    private readonly Dictionary<int, DateTime> _seen = [];
    private readonly HashSet<int> _approved = [];
    private readonly Dictionary<Guid, PendingInstall> _pending = [];
    private readonly Dictionary<string, bool> _installerCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _firstScan = true;

    private sealed class PendingInstall
    {
        public Guid Id { get; init; }
        public int Session { get; init; }
        public List<int> Pids { get; } = [];
        public string Name { get; init; } = string.Empty;
        public string? Path { get; init; }
        public string? Description { get; init; }
        public DateTime DetectedAt { get; init; }
        public int Failures { get; set; }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(DetectLoop(stoppingToken), PipeLoop(stoppingToken));

    private bool Active => state.Current is { InstallBlocked: true, ProtectionHash.Length: > 0 };

    // ---------- Detection ----------

    private async Task DetectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!Active)
                {
                    ReleaseAll();
                    await Task.Delay(1000, ct);
                    continue;
                }
                Scan();
                ExpirePending();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Install guard scan failed");
            }
            await Task.Delay(50, ct);
        }
    }

    private void Scan()
    {
        var alive = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                int session;
                DateTime started;
                try
                {
                    session = p.SessionId;
                    started = p.StartTime;
                }
                catch
                {
                    continue;
                }
                if (session == 0 && !IsUserFacingSessionZero(p))
                    continue;

                alive.Add(p.Id);
                lock (_gate)
                {
                    if (_seen.TryGetValue(p.Id, out var known) && known == started) continue;
                    _seen[p.Id] = started;
                }
                if (!_firstScan)
                {
                    var console = Native.ActiveSession();
                    var promptSession = session == 0 && console > 0 ? console : session;
                    Evaluate(p, promptSession);
                }
            }
            finally
            {
                p.Dispose();
            }
        }
        lock (_gate)
        {
            foreach (var pid in _seen.Keys.Where(k => !alive.Contains(k)).ToList()) _seen.Remove(pid);
            _approved.RemoveWhere(pid => !alive.Contains(pid));
            foreach (var g in _pending.Values.ToList())
            {
                g.Pids.RemoveAll(pid => !alive.Contains(pid));
                if (g.Pids.Count == 0) _pending.Remove(g.Id);
            }
        }
        // Programs already running when the lock was switched on are left alone.
        _firstScan = false;
    }

    private void Evaluate(Process p, int session)
    {
        var pid = p.Id;
        var parent = Native.ParentPid(pid);
        lock (_gate)
        {
            if (parent is { } pp && _approved.Contains(pp))
            {
                _approved.Add(pid);
                return;
            }
            var parentGroup = parent is { } gp ? _pending.Values.FirstOrDefault(g => g.Pids.Contains(gp)) : null;
            if (parentGroup is not null)
            {
                Native.Suspend(pid);
                parentGroup.Pids.Add(pid);
                return;
            }
        }

        var name = p.ProcessName;
        var path = Native.ImagePath(pid);

        // KOSHI's own upgrade spawns helpers from Windows\Temp; freezing them stalls the update.
        // Only while a KOSHI setup is actually alive — never a blanket pause.
        if (UpgradeGate.SetupRunning && IsKoshiSetupHelper(path))
        {
            lock (_gate) _approved.Add(pid);
            return;
        }

        // Never freeze, password-gate or signature-scan KOSHI's own updater — that would lock the setup file.
        if (AgentSelfUpdate.IsSetup(name, path) || IsTrustedKoshiSetup(name, path))
        {
            lock (_gate) _approved.Add(pid);
            UpgradeGate.Enter(TimeSpan.FromMinutes(15));
            SelfHeal.BeginUpgrade();
            return;
        }

        if (!IsInstaller(name, path, out var description)) return;

        Native.Suspend(pid);
        var pending = new PendingInstall
        {
            Id = Guid.NewGuid(), Session = session, Name = name, Path = path, Description = description, DetectedAt = DateTime.UtcNow,
        };
        pending.Pids.Add(pid);
        lock (_gate) _pending[pending.Id] = pending;
        logger.LogInformation("Installer {Name} ({Path}) frozen in session {Session}; waiting for password", name, path, session);
    }

    /// <summary>
    /// Session-0 msiexec is used by Windows Update. Only intercept it when the package lives in a user folder/temp.
    /// </summary>
    private static bool IsUserFacingSessionZero(Process p)
    {
        var name = p.ProcessName.ToLowerInvariant();
        if (!InstallerHosts.Contains(name) && !NameKeywords.Any(name.Contains))
            return false;
        var blob = (UninstallGuard.ReadCommandLine(p.Id) ?? "") + " " + (Native.ImagePath(p.Id) ?? "");
        return blob.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase)
            || blob.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase)
            || blob.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInstaller(string processName, string? path, out string? description)
    {
        description = null;
        var name = processName.ToLowerInvariant();
        if (AgentSelfUpdate.IsSetup(processName, path))
            return false;
        if (name.StartsWith("unins", StringComparison.Ordinal) || name.Contains("uninst", StringComparison.Ordinal)) return false;
        if (InstallerHosts.Contains(name)) return true;
        if (NameKeywords.Any(name.Contains)) return true;
        if (path is null) return false;
        if (IsHotDropPath(path)) return true;
        if (IsUnderTrustedDirectory(path)) return false;

        FileVersionInfo? vi = null;
        try { vi = FileVersionInfo.GetVersionInfo(path); }
        catch (FileNotFoundException) { }
        description = vi?.FileDescription is { Length: > 0 } d ? d : vi?.ProductName;

        var meta = vi is null ? string.Empty
            : string.Join(' ', vi.FileDescription, vi.ProductName, vi.InternalName, vi.OriginalFilename, vi.Comments, vi.CompanyName).ToLowerInvariant();
        if (MetaMarks.Any(meta.Contains)) return true;
        var ext = Path.GetExtension(path);
        if (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".msix", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".appx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".appxbundle", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase))
            return true;
        return HasInstallerSignature(path);
    }

    /// <summary>
    /// Typical "I just downloaded this" locations. The service runs as SYSTEM so we match
    /// every user's Downloads/Desktop/Temp rather than Environment.SpecialFolder.
    /// </summary>
    private static bool IsHotDropPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(Path.GetTempPath().TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
            var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (full.StartsWith(winTemp.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;

            var users = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\", "Users");
            if (full.StartsWith(users.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[(users.TrimEnd('\\').Length + 1)..];
                var slash = rel.IndexOf('\\');
                if (slash > 0)
                {
                    var rest = rel[(slash + 1)..];
                    if (rest.StartsWith("Downloads\\", StringComparison.OrdinalIgnoreCase)
                        || rest.StartsWith("Desktop\\", StringComparison.OrdinalIgnoreCase)
                        || rest.StartsWith("Documents\\", StringComparison.OrdinalIgnoreCase)
                        || rest.StartsWith(@"AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase)
                        || rest.StartsWith(@"AppData\Local\Microsoft\Windows\INetCache\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.DriveType is DriveType.Removable or DriveType.CDRom or DriveType.Ram)
                    return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
        return false;
    }

    private static bool IsUnderTrustedDirectory(string path)
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? AppContext.BaseDirectory,
        ];
        return roots.Any(r => !string.IsNullOrEmpty(r) && path.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Looks for installer-builder markers in the first 4 MB of the executable. Cached per file version.</summary>
    private bool HasInstallerSignature(string path)
    {
        if (AgentSelfUpdate.IsSetupPath(path))
            return false;
        try
        {
            var fi = new FileInfo(path);
            var cacheKey = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            lock (_gate)
            {
                if (_installerCache.TryGetValue(cacheKey, out var cached)) return cached;
            }
            var buffer = new byte[(int)Math.Min(fi.Length, 4 * 1024 * 1024)];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                fs.ReadExactly(buffer);
            var found = BinaryMarks.Any(m => buffer.AsSpan().IndexOf(m) >= 0);
            lock (_gate)
            {
                if (_installerCache.Count > 2000) _installerCache.Clear();
                _installerCache[cacheKey] = found;
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsKoshiSetupHelper(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var file = Path.GetFileName(path);
        if (file.Equals("resolutionhost.exe", StringComparison.OrdinalIgnoreCase)) return true;
        // Inno Setup extracts its helpers to %TEMP%\is-XXXXX.tmp\.
        return path.Contains(@"\is-", StringComparison.OrdinalIgnoreCase) && path.Contains(@".tmp\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>KOSHI's own updater (started by the tray) is allowed when its SHA-256 matches the published release.</summary>
    private bool IsTrustedKoshiSetup(string name, string? path)
    {
        if (AgentSelfUpdate.IsSetup(name, path)) return true;
        if (path is null) return false;
        try
        {
            var file = Path.GetFileName(path);
            if (AgentSelfUpdate.IsSetupPath(path) || file.StartsWith(AgentSelfUpdate.SetupPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            var vi = FileVersionInfo.GetVersionInfo(path);
            var meta = string.Join(' ', vi.ProductName, vi.FileDescription, vi.OriginalFilename, vi.CompanyName);
            return meta.Contains("KOSHI", StringComparison.OrdinalIgnoreCase)
                   && meta.Contains("setup", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return AgentSelfUpdate.IsSetupProcess(name);
        }
    }

    private void ExpirePending()
    {
        var now = DateTime.UtcNow;
        List<PendingInstall> expired;
        lock (_gate)
        {
            expired = _pending.Values.Where(g => now - g.DetectedAt > PendingTimeout).ToList();
            foreach (var g in expired) _pending.Remove(g.Id);
        }
        foreach (var g in expired)
        {
            logger.LogInformation("Installer {Name} not approved in time; closed", g.Name);
            Kill(g);
        }
    }

    /// <summary>Lock switched off: let frozen installers continue.</summary>
    private void ReleaseAll()
    {
        List<PendingInstall> all;
        lock (_gate)
        {
            all = [.. _pending.Values];
            _pending.Clear();
            _seen.Clear();
            _approved.Clear();
            _firstScan = true;
        }
        foreach (var g in all) g.Pids.ForEach(Native.Resume);
    }

    private static void Kill(PendingInstall g)
    {
        foreach (var pid in g.Pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already exited
            }
        }
    }

    // ---------- Pipe (tray agent) ----------

    private async Task PipeLoop(CancellationToken ct)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = NamedPipeServerStreamAcl.Create(GatePipe.Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                await server.WaitForConnectionAsync(ct);
                var connection = server;
                server = null;
                _ = Task.Run(() => HandleAsync(connection, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Gate pipe accept failed");
                await Task.Delay(1000, ct);
            }
            finally
            {
                if (server is not null) await server.DisposeAsync();
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var line = await GatePipe.ReadLineAsync(pipe, cts.Token);
                var request = line is null ? null : JsonSerializer.Deserialize<GateRequest>(line, KoshiApiClient.Json);
                if (request is null) return;
                var session = Native.ClientSession(pipe);
                var response = session is { } s ? Handle(request, s) : new GateResponse { Ok = false, Message = "Sessiya aniqlanmadi" };
                await GatePipe.WriteLineAsync(pipe, JsonSerializer.Serialize(response, KoshiApiClient.Json), cts.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
            {
                // Client went away
            }
        }
    }

    private GateResponse Handle(GateRequest r, int session)
    {
        switch (r.Op)
        {
            case GatePipe.OpRefresh:
                state.RequestRefresh();
                return new GateResponse { Ok = true };
            case GatePipe.OpPrepareUpgrade:
                UpgradeGate.Enter(TimeSpan.FromMinutes(15));
                SelfHeal.BeginUpgrade();
                return new GateResponse { Ok = true };
            case GatePipe.OpRunSetup:
                return StartTrustedSetup(r.Path);
            case GatePipe.OpUninstall:
                if (!SelfUninstall.Begin(lifetime))
                    return new GateResponse { Ok = true, Message = "O‘chirish allaqachon boshlangan" };
                logger.LogWarning("Remote uninstall started by administrator");
                return new GateResponse { Ok = true, Message = "KOSHI bu kompyuterdan o‘chirilmoqda" };
            case GatePipe.OpPending:
                lock (_gate)
                {
                    return new GateResponse
                    {
                        Ok = true,
                        Pending = _pending.Values.Where(g => g.Session == session)
                            .Select(g => new GatePendingInstall(g.Id, g.Name, g.Path, g.Description, g.DetectedAt)).ToList(),
                    };
                }
            case GatePipe.OpApprove when r.Id is { } id:
                return Approve(id, r.Password ?? string.Empty, session);
            case GatePipe.OpDeny when r.Id is { } id:
                PendingInstall? denied;
                lock (_gate)
                {
                    if (!_pending.TryGetValue(id, out denied) || denied.Session != session) return new GateResponse { Closed = true };
                    _pending.Remove(id);
                }
                Kill(denied);
                logger.LogInformation("Installer {Name} cancelled by user", denied.Name);
                return new GateResponse { Ok = true, Closed = true };
            default:
                return new GateResponse { Ok = false, Message = "Noma’lum so‘rov" };
        }
    }

    private GateResponse StartTrustedSetup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new GateResponse { Ok = false, Message = "O‘rnatuvchi topilmadi" };
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return new GateResponse { Ok = false, Message = "Noto‘g‘ri yo‘l" }; }
        if (!AgentSelfUpdate.IsSetupPath(full))
            return new GateResponse { Ok = false, Message = "Noto‘g‘ri o‘rnatuvchi" };
        var roots = new[]
        {
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(AgentPaths.MachineDir),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        };
        if (!roots.Any(r => full.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            return new GateResponse { Ok = false, Message = "Noto‘g‘ri yo‘l" };

        UpgradeGate.Enter(TimeSpan.FromMinutes(15));
        SelfHeal.BeginUpgrade();
        try
        {
            AgentSelfUpdate.Launch(full, alreadyElevated: true);
            logger.LogInformation("Trusted KOSHI setup started as SYSTEM: {Path}", full);
            return new GateResponse { Ok = true, Message = "Yangilanish o‘rnatilmoqda" };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trusted setup launch failed");
            return new GateResponse { Ok = false, Message = ex.Message };
        }
    }

    private GateResponse Approve(Guid id, string password, int session)
    {
        var policy = state.Current;
        PendingInstall? g;
        lock (_gate)
        {
            if (!_pending.TryGetValue(id, out g) || g.Session != session) return new GateResponse { Closed = true, Message = "O‘rnatish so‘rovi topilmadi" };
        }
        if (policy is not null && AppLockPassword.Verify(policy.ProtectionSalt, policy.ProtectionHash, password))
        {
            lock (_gate)
            {
                _pending.Remove(id);
                foreach (var pid in g.Pids) _approved.Add(pid);
            }
            g.Pids.ForEach(Native.Resume);
            logger.LogInformation("Installer {Name} approved with password; further setups will ask again", g.Name);
            return new GateResponse { Ok = true, Closed = true, Message = "Shu o‘rnatishga ruxsat. Keyingi dastur uchun parol qayta so‘raladi." };
        }

        bool exhausted;
        lock (_gate)
        {
            g.Failures++;
            exhausted = g.Failures >= MaxAttempts;
            if (exhausted) _pending.Remove(id);
        }
        logger.LogWarning("Wrong install password for {Name} ({Failures}/{Max})", g.Name, g.Failures, MaxAttempts);
        if (!exhausted) return new GateResponse { Ok = false, Message = $"Parol noto‘g‘ri ({g.Failures}/{MaxAttempts})" };
        Kill(g);
        return new GateResponse { Ok = false, Closed = true, Message = "Parol 3 marta noto‘g‘ri kiritildi — o‘rnatish bekor qilindi" };
    }

    // ---------- Native ----------

    private static partial class Native
    {
        private const uint ProcessSuspendResume = 0x0800;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public nint ExitStatus;
            public nint PebBaseAddress;
            public nint AffinityMask;
            public nint BasePriority;
            public nint UniqueProcessId;
            public nint InheritedFromUniqueProcessId;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("ntdll.dll")]
        private static partial int NtSuspendProcess(nint handle);

        [LibraryImport("ntdll.dll")]
        private static partial int NtResumeProcess(nint handle);

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(nint handle, int infoClass, ref ProcessBasicInformation info, int length, out int returnLength);

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool QueryFullProcessImageName(nint handle, uint flags, [Out] char[] buffer, ref uint size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetNamedPipeClientSessionId(nint pipe, out uint sessionId);

        public static void Suspend(int pid) => WithHandle(pid, ProcessSuspendResume, h => NtSuspendProcess(h));

        public static void Resume(int pid) => WithHandle(pid, ProcessSuspendResume, h => NtResumeProcess(h));

        public static int? ParentPid(int pid)
        {
            int? parent = null;
            WithHandle(pid, ProcessQueryLimitedInformation, h =>
            {
                var info = default(ProcessBasicInformation);
                if (NtQueryInformationProcess(h, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0)
                    parent = (int)info.InheritedFromUniqueProcessId;
                return 0;
            });
            return parent;
        }

        public static string? ImagePath(int pid)
        {
            string? path = null;
            WithHandle(pid, ProcessQueryLimitedInformation, h =>
            {
                var buffer = new char[1024];
                var size = (uint)buffer.Length;
                if (QueryFullProcessImageName(h, 0, buffer, ref size)) path = new string(buffer, 0, (int)size);
                return 0;
            });
            return path;
        }

        public static int? ClientSession(NamedPipeServerStream pipe) =>
            GetNamedPipeClientSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out var session) ? (int)session : null;

        [LibraryImport("kernel32.dll")]
        private static partial uint WTSGetActiveConsoleSessionId();

        public static int ActiveSession()
        {
            var id = WTSGetActiveConsoleSessionId();
            return id is 0 or 0xFFFFFFFF ? 0 : (int)id;
        }

        private static void WithHandle(int pid, uint access, Func<nint, int> action)
        {
            var h = OpenProcess(access, false, (uint)pid);
            if (h == 0) return;
            try { action(h); }
            finally { CloseHandle(h); }
        }
    }
}
