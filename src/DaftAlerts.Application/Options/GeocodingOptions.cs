namespace DaftAlerts.Application.Options;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    public string Provider { get; set; } = "GoogleThenNominatim";
    public string? GoogleApiKey { get; set; }
    public string NominatimUserAgent { get; set; } = "DaftAlerts/1.0";
    public int CacheTtlDays { get; set; } = 365;
}
