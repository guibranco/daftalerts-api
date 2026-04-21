using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Options;
using DaftAlerts.Infrastructure.Geocoding;
using DaftAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DaftAlerts.Infrastructure.Tests.Geocoding;

public sealed class GeocodingServiceTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            CallCount++;
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private HybridGeocodingService CreateService(
        StubHandler googleHandler,
        StubHandler nominatimHandler,
        string? googleKey
    )
    {
        var opts = Options.Create(
            new GeocodingOptions
            {
                GoogleApiKey = googleKey,
                NominatimUserAgent = "DaftAlerts/1.0 (test)",
                CacheTtlDays = 365,
            }
        );
        var google = new GoogleGeocoder(new HttpClient(googleHandler), opts);
        var nominatim = new NominatimGeocoder(new HttpClient(nominatimHandler), opts);
        return new HybridGeocodingService(
            google,
            nominatim,
            _db,
            opts,
            new FixedClock(),
            NullLogger<HybridGeocodingService>.Instance
        );
    }

    [Fact]
    public async Task Returns_google_result_and_caches_it()
    {
        var google = new StubHandler
        {
            Responder = _ =>
                JsonOk(
                    """{"status":"OK","results":[{"geometry":{"location":{"lat":53.33,"lng":-6.25}}}]}"""
                ),
        };
        var nominatim = new StubHandler();
        var svc = CreateService(google, nominatim, googleKey: "key");

        var first = await svc.GeocodeAsync(
            "Herbert Lane Mews, Dublin 2",
            "D02KC86",
            CancellationToken.None
        );
        first.Should().NotBeNull();
        first!.Value.Provider.Should().Be("google");
        google.CallCount.Should().Be(1);

        // Second call should hit cache, not Google.
        var second = await svc.GeocodeAsync(
            "Herbert Lane Mews, Dublin 2",
            "D02KC86",
            CancellationToken.None
        );
        second.Should().NotBeNull();
        second!.Value.Provider.Should().Contain("cached");
        google.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Falls_back_to_nominatim_when_google_fails()
    {
        var google = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var nominatim = new StubHandler
        {
            Responder = _ => JsonOk("""[{"lat":"53.34","lon":"-6.26"}]"""),
        };
        var svc = CreateService(google, nominatim, googleKey: "key");

        var result = await svc.GeocodeAsync("Some addr", "D01X1X1", CancellationToken.None);
        result.Should().NotBeNull();
        result!.Value.Provider.Should().Be("nominatim");
        nominatim.CallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Uses_nominatim_only_when_no_google_key()
    {
        var google = new StubHandler();
        var nominatim = new StubHandler
        {
            Responder = _ => JsonOk("""[{"lat":"53.0","lon":"-6.0"}]"""),
        };
        var svc = CreateService(google, nominatim, googleKey: null);

        var result = await svc.GeocodeAsync("Addr", null, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Value.Provider.Should().Be("nominatim");
        google.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Returns_null_when_both_providers_fail()
    {
        var google = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var nominatim = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var svc = CreateService(google, nominatim, googleKey: "key");

        var result = await svc.GeocodeAsync("Addr", null, CancellationToken.None);
        result.Should().BeNull();
    }
}
