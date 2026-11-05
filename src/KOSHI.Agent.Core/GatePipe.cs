using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace KOSHI.Agent.Core;

/// <summary>
/// Local channel between the SYSTEM service and the tray agent. The service freezes a starting installer and the tray
/// asks the user for the protection password; the password is verified by the service, never by the tray.
/// </summary>
public static class GatePipe
{
    public const string Name = "KOSHI.Agent.Gate";

    public const string OpPending = "pending";
    public const string OpApprove = "approve";
    public const string OpDeny = "deny";
    public const string OpRefresh = "refresh";
    public const string OpPrepareUpgrade = "prepare-upgrade";
    public const string OpRunSetup = "run-setup";
    public const string OpUninstall = "uninstall-self";

    public static async Task<GateResponse?> SendAsync(GateRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);
            await WriteLineAsync(pipe, JsonSerializer.Serialize(request, KoshiApiClient.Json), cts.Token);
            var line = await ReadLineAsync(pipe, cts.Token);
            return line is null ? null : JsonSerializer.Deserialize<GateResponse>(line, KoshiApiClient.Json);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteLineAsync(Stream stream, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one UTF-8 line (max 64 KB).</summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(256);
        var one = new byte[1];
        while (buffer.Count < 65536)
        {
            var n = await stream.ReadAsync(one, ct);
            if (n == 0) break;
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(buffer.ToArray());
            buffer.Add(one[0]);
        }
        return buffer.Count > 0 ? Encoding.UTF8.GetString(buffer.ToArray()) : null;
    }
}

public sealed class GateRequest
{
    public string Op { get; set; } = GatePipe.OpPending;
    public Guid? Id { get; set; }
    public string? Password { get; set; }
    public string? Path { get; set; }
}

/// <summary>An installer frozen by the service until the protection password is entered.</summary>
public sealed record GatePendingInstall(Guid Id, string Name, string? Path, string? Description, DateTime DetectedAt);

public sealed class GateResponse
{
    public bool Ok { get; set; }
    public string? Message { get; set; }

    /// <summary>True when the request no longer exists (installer was closed, timed out or denied).</summary>
    public bool Closed { get; set; }
    public List<GatePendingInstall> Pending { get; set; } = [];
}
