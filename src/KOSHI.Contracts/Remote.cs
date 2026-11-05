namespace KOSHI.Contracts;

/// <summary>Whitelisted remote commands. Anything else is rejected by the server and the agent.</summary>
public static class RemoteCommands
{
    public const string Lock = "lock";
    public const string Message = "message";
    public const string Logoff = "logoff";
    public const string Restart = "restart";
    public const string Shutdown = "shutdown";
    public const string CancelShutdown = "cancel-shutdown";
    public const string KillProcess = "kill-process";
    public const string RefreshPolicy = "refresh-policy";
    public const string LocateNow = "locate-now";
    public const string UninstallSelf = "uninstall-self";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Lock, Message, Logoff, Restart, Shutdown, CancelShutdown, KillProcess, RefreshPolicy, LocateNow, UninstallSelf,
    };
}

/// <summary>Mouse/keyboard event from the admin viewer. Coordinates are normalised to 0..1 of the remote screen.</summary>
public sealed record RemoteInputDto(string Type, double X, double Y, int Button, int Delta, int KeyCode);

public sealed record RemoteCommandDto(Guid CommandId, string Command, string? Argument, string RequestedBy);

public sealed record RemoteSessionStartDto(Guid SessionId, string AdminName, int MaxWidth, int Quality, int Fps);
