using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace KOSHI.Agent.Core;

/// <summary>Source: gps / wifi / cell / windows (Windows Location service).</summary>
public sealed record LocationReading(double Latitude, double Longitude, double? AccuracyMeters, string Source);

/// <summary>Implemented by the tray app with the Windows Location API (GPS receiver or Wi-Fi positioning).</summary>
public interface ILocationProvider
{
    /// <summary>Returns null when Windows location is turned off, denied or unavailable.</summary>
    Task<LocationReading?> GetAsync(CancellationToken ct);
}

public sealed record IpLocation(double Latitude, double Longitude, string? City, string? Region, string? Country, string? Isp);

/// <summary>Approximate (city-level) position from the public IP address; used when Windows location is unavailable.</summary>
public static class IpLocationLookup
{
    public const double ApproximateAccuracyMeters = 5000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<IpLocation?> LookupAsync(CancellationToken ct)
    {
        try
        {
            var r = await Http.GetFromJsonAsync<IpWhoResponse>("https://ipwho.is/", ct);
            if (r is not { Success: true, Latitude: { } lat, Longitude: { } lon }) return null;
            return new IpLocation(lat, lon, r.City, r.Region, r.Country, r.Connection?.Isp);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class IpWhoResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("latitude")] public double? Latitude { get; set; }
        [JsonPropertyName("longitude")] public double? Longitude { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("connection")] public IpWhoConnection? Connection { get; set; }
    }

    private sealed class IpWhoConnection
    {
        [JsonPropertyName("isp")] public string? Isp { get; set; }
    }
}
