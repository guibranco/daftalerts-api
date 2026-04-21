using System.Threading;
using System.Threading.Tasks;

namespace DaftAlerts.Application.Abstractions;

public readonly record struct GeoPoint(double Latitude, double Longitude, string Provider);

public interface IGeocodingService
{
    /// <summary>
    /// Attempts to resolve an address + eircode to a latitude/longitude. Returns null if no provider could resolve it.
    /// Implementations are expected to cache results.
    /// </summary>
    Task<GeoPoint?> GeocodeAsync(string address, string? eircode, CancellationToken ct);
}
