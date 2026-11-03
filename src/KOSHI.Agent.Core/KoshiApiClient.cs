using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KOSHI.Contracts;

namespace KOSHI.Agent.Core;

public enum ServerRejection { None, Blocked, Revoked, Invalid }

/// <summary>Thrown when the server refuses the device (blocked / credential revoked); retrying will not help.</summary>
public sealed class DeviceRejectedException(ServerRejection reason, string message) : Exception(message)
{
    public ServerRejection Reason { get; } = reason;
}

public sealed class KoshiApiClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly HttpClient _http;

    public KoshiApiClient(string serverUrl, string? credential, HttpMessageHandler? handler = null)
    {
        var uri = new Uri(serverUrl.TrimEnd('/') + "/");
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ArgumentException("Server URL must use HTTPS.", nameof(serverUrl));
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = uri;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"KOSHI-Agent/{AgentInfo.Version}");
        if (credential is not null) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", credential);
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public Task<DeviceActivationResponse> ActivateAsync(DeviceActivationRequest r, CancellationToken ct) =>
        SendAsync<DeviceActivationResponse>(HttpMethod.Post, "api/devices/activate", r, ct);

    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest r, CancellationToken ct) =>
        SendAsync<HeartbeatResponse>(HttpMethod.Post, "api/agent/heartbeat", r, ct);

    public Task<TelemetryBatchResponse> SendBatchAsync(TelemetryBatchRequest r, CancellationToken ct) =>
        SendAsync<TelemetryBatchResponse>(HttpMethod.Post, "api/telemetry/batch", r, ct);

    public Task<AgentPolicyDto> GetPolicyAsync(CancellationToken ct) =>
        SendAsync<AgentPolicyDto>(HttpMethod.Get, "api/agent/policy", null, ct);

    public Task ReportPolicyResultAsync(PolicyResultReportRequest r, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Post, "api/agent/policy-results", r, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
        {
            if (res.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
            return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }

        var code = await ReadCodeAsync(res, ct);
        throw res.StatusCode switch
        {
            HttpStatusCode.Forbidden when code == ErrorCodes.DeviceBlocked => new DeviceRejectedException(ServerRejection.Blocked, "Qurilma administrator tomonidan bloklangan."),
            HttpStatusCode.Unauthorized when code == ErrorCodes.CredentialRevoked => new DeviceRejectedException(ServerRejection.Revoked, "Qurilma credential'i bekor qilingan."),
            HttpStatusCode.Unauthorized => new DeviceRejectedException(ServerRejection.Invalid, "Qurilma autentifikatsiyadan o'tmadi."),
            HttpStatusCode.BadRequest when code == ErrorCodes.ActivationInvalid => new DeviceRejectedException(ServerRejection.Invalid, "Aktivatsiya kodi noto'g'ri, muddati o'tgan yoki ishlatilgan."),
            _ => new HttpRequestException($"Server returned {(int)res.StatusCode} ({code ?? "no code"})", null, res.StatusCode),
        };
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

public static class AgentInfo
{
    public static string Version => typeof(AgentInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Build date (yyyy-MM-dd) embedded as the metadata part of the informational version, e.g. "1.0.3+20261103".</summary>
    public static string BuildDate
    {
        get
        {
            var info = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AgentInfo).Assembly)?.InformationalVersion ?? string.Empty;
            var plus = info.IndexOf('+');
            if (plus >= 0 && DateTime.TryParseExact(new string(info[(plus + 1)..].Where(char.IsDigit).ToArray()).PadRight(8).TrimEnd(),
                    "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                return d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return string.Empty;
        }
    }

    public static string VersionWithDate => string.IsNullOrEmpty(BuildDate) ? Version : $"{Version} ({BuildDate})";
}
