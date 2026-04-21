using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Options;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DaftAlerts.Infrastructure.Geocoding;

/// <summary>
/// Primary -> fallback chain with caching. Google first (paid, Eircode-accurate); Nominatim on failure.
/// Results are cached for <c>GeocodingOptions.CacheTtlDays</c>.
/// </summary>
public sealed class HybridGeocodingService : IGeocodingService
{
    private readonly GoogleGeocoder _google;
    private readonly NominatimGeocoder _nominatim;
    private readonly AppDbContext _db;
    private readonly GeocodingOptions _options;
    private readonly ILogger<HybridGeocodingService> _logger;
    private readonly IClock _clock;

    public HybridGeocodingService(
        GoogleGeocoder google,
        NominatimGeocoder nominatim,
        AppDbContext db,
        IOptions<GeocodingOptions> options,
        IClock clock,
        ILogger<HybridGeocodingService> logger)
    {
        _google = google;
        _nominatim = nominatim;
        _db = db;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<GeoPoint?> GeocodeAsync(string address, string? eircode, CancellationToken ct)
    {
        var key = BuildCacheKey(address, eircode);
        var now = _clock.UtcNow;

        // 1. Cache
        var cached = await _db.GeocodeCaches.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
        if (cached is not null && cached.ExpiresAt > now)
        {
            _logger.LogDebug("Geocode cache hit for key={Key}", key);
            return new GeoPoint(cached.Latitude, cached.Longitude, cached.Provider + "(cached)");
        }

        var query = BuildQueryString(address, eircode);

        // 2. Google
        if (!string.IsNullOrWhiteSpace(_options.GoogleApiKey))
        {
            var googleResult = await SafeCall(_google, query, "google", ct);
            if (googleResult is not null)
            {
                await WriteCacheAsync(key, googleResult.Value, ct);
                return googleResult;
            }
        }

        // 3. Nominatim
        var nominatimResult = await SafeCall(_nominatim, query, "nominatim", ct);
        if (nominatimResult is not null)
        {
            await WriteCacheAsync(key, nominatimResult.Value, ct);
            return nominatimResult;
        }

        _logger.LogWarning("Geocoding failed for address={Address}, eircode={Eircode}", address, eircode);
        return null;
    }

    private async Task<GeoPoint?> SafeCall(IGeocodeProvider provider, string query, string name, CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await provider.GeocodeAsync(query, ct);
            sw.Stop();
            _logger.LogInformation("Geocode via {Provider} took {Elapsed}ms hit={Hit}", name, sw.ElapsedMilliseconds, result is not null);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geocode provider {Provider} threw", name);
            return null;
        }
    }

    private async Task WriteCacheAsync(string key, GeoPoint point, CancellationToken ct)
    {
        var entity = await _db.GeocodeCaches.FirstOrDefaultAsync(c => c.Key == key, ct);
        var now = _clock.UtcNow;
        if (entity is null)
        {
            _db.GeocodeCaches.Add(new GeocodeCache
            {
                Key = key,
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                Provider = point.Provider,
                CreatedAt = now,
                ExpiresAt = now.AddDays(Math.Max(1, _options.CacheTtlDays))
            });
        }
        else
        {
            entity.Latitude = point.Latitude;
            entity.Longitude = point.Longitude;
            entity.Provider = point.Provider;
            entity.CreatedAt = now;
            entity.ExpiresAt = now.AddDays(Math.Max(1, _options.CacheTtlDays));
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string BuildQueryString(string address, string? eircode)
    {
        var pieces = new List<string>();
        if (!string.IsNullOrWhiteSpace(address)) pieces.Add(address.Trim());
        if (!string.IsNullOrWhiteSpace(eircode) && !address.Contains(eircode, StringComparison.OrdinalIgnoreCase))
            pieces.Add(eircode);
        pieces.Add("Ireland");
        return string.Join(", ", pieces);
    }

    private static string BuildCacheKey(string address, string? eircode)
    {
        var raw = (address + "|" + (eircode ?? string.Empty)).ToLowerInvariant().Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}

public interface IGeocodeProvider
{
    Task<GeoPoint?> GeocodeAsync(string query, CancellationToken ct);
}

// -----------------------------------------------------------------------------
// Google
// -----------------------------------------------------------------------------

public sealed class GoogleGeocoder : IGeocodeProvider
{
    private readonly HttpClient _http;
    private readonly GeocodingOptions _options;

    public GoogleGeocoder(HttpClient http, IOptions<GeocodingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.GoogleApiKey)) return null;

        var url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                  $"?address={Uri.EscapeDataString(query)}" +
                  $"&region=ie" +
                  $"&key={Uri.EscapeDataString(_options.GoogleApiKey!)}";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var payload = await resp.Content.ReadFromJsonAsync<GoogleGeocodeResponse>(ct);
        if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase)) return null;
        var first = payload.Results?.FirstOrDefault();
        if (first?.Geometry?.Location is null) return null;

        return new GeoPoint(first.Geometry.Location.Lat, first.Geometry.Location.Lng, "google");
    }

    internal sealed record GoogleGeocodeResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] IReadOnlyList<GoogleResult>? Results);

    internal sealed record GoogleResult(
        [property: JsonPropertyName("geometry")] GoogleGeometry? Geometry);

    internal sealed record GoogleGeometry(
        [property: JsonPropertyName("location")] GoogleLatLng? Location);

    internal sealed record GoogleLatLng(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lng")] double Lng);
}

// -----------------------------------------------------------------------------
// Nominatim
// -----------------------------------------------------------------------------

public sealed class NominatimGeocoder : IGeocodeProvider
{
    // Nominatim usage policy: max 1 request/second.
    private static readonly SemaphoreSlim RateLimiter = new(1, 1);

    private readonly HttpClient _http;
    private readonly GeocodingOptions _options;

    public NominatimGeocoder(HttpClient http, IOptions<GeocodingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken ct)
    {
        await RateLimiter.WaitAsync(ct);
        try
        {
            // Simple 1 req/sec cadence
            var before = DateTime.UtcNow;
            var url = $"https://nominatim.openstreetmap.org/search" +
                      $"?q={Uri.EscapeDataString(query)}" +
                      $"&format=json&countrycodes=ie&limit=1";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(_options.NominatimUserAgent);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var results = await resp.Content.ReadFromJsonAsync<IReadOnlyList<NominatimResult>>(ct);
            var first = results?.FirstOrDefault();
            if (first is null) return null;

            if (!double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;

            var elapsed = DateTime.UtcNow - before;
            if (elapsed < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, ct);

            return new GeoPoint(lat, lon, "nominatim");
        }
        finally
        {
            RateLimiter.Release();
        }
    }

    internal sealed record NominatimResult(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon);
}
