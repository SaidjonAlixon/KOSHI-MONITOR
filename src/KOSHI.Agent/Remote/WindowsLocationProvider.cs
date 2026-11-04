using KOSHI.Agent.Core;
using Windows.Devices.Geolocation;

namespace KOSHI.Agent.Remote;

/// <summary>
/// Windows Location API: uses a GPS receiver when the device has one, otherwise Wi-Fi / cell positioning.
/// Requires Windows "Location services" and "Let desktop apps access your location" to be on.
/// </summary>
public sealed class WindowsLocationProvider : ILocationProvider
{
    public async Task<LocationReading?> GetAsync(CancellationToken ct)
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            var locator = new Geolocator { DesiredAccuracyInMeters = 30 };
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(25)).AsTask(ct);
            var c = pos.Coordinate;
            var source = c.PositionSource switch
            {
                PositionSource.Satellite => "gps",
                PositionSource.WiFi => "wifi",
                PositionSource.Cellular => "cell",
                PositionSource.IPAddress => "ip",
                _ => "windows",
            };
            return new LocationReading(c.Point.Position.Latitude, c.Point.Position.Longitude, c.Accuracy, source);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Location turned off, access denied or no positioning hardware: caller falls back to IP location.
            return null;
        }
    }
}
