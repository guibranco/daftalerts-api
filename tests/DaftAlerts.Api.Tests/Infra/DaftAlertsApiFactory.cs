using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DaftAlerts.Api.Tests.Infra;

public sealed class DaftAlertsApiFactory : WebApplicationFactory<Program>
{
    public const string TestToken = "test-token-abcdef";

    // Named in-memory SQLite with shared cache so multiple EF Core connections share data
    // while each gets its own SqliteConnection object (avoids concurrent CreateFunction crashes).
    private readonly string _connString;
    private readonly SqliteConnection _keeper;

    public DaftAlertsApiFactory()
    {
        var dbName = $"testdb{Guid.NewGuid():N}";
        _connString = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        // Keep one connection open so the named in-memory DB isn't dropped between EF Core scopes.
        _keeper = new SqliteConnection(_connString);
        _keeper.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Create schema once after the host (and DI container) is ready.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(
            (_, config) =>
            {
                var testDbPath = Path.Combine(
                    Path.GetTempPath(),
                    $"daftalerts-test-{Guid.NewGuid():N}.db"
                );

                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] =
                            $"Data Source={testDbPath};Cache=Shared;Foreign Keys=true",
                        ["Auth:ApiToken"] = TestToken,
                        ["Geocoding:GoogleApiKey"] = "",
                    }
                );
            }
        );

        builder.ConfigureServices(services =>
        {
            // Replace the production DbContext with the shared in-memory named database.
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connString));

            // Replace geocoding with a noop — we don't want network calls in tests.
            var geoDescriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IGeocodingService)
            );
            if (geoDescriptor is not null)
                services.Remove(geoDescriptor);
            services.AddScoped<IGeocodingService, NoopGeocodingService>();
        });
    }

    public HttpClient CreateAuthedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestToken
        );
        return client;
    }

    public AppDbContext CreateDbContext()
    {
        // Accessing Services forces the host to start, ensuring EnsureCreated() has run.
        _ = Services;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connString).Options;
        return new AppDbContext(options);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _keeper.Dispose();
        try
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), "daftalerts-test-*.db*"))
                File.Delete(file);
        }
        catch
        { /* ignore */
        }
    }
}

internal sealed class NoopGeocodingService : IGeocodingService
{
    public Task<GeoPoint?> GeocodeAsync(string address, string? eircode, CancellationToken ct) =>
        Task.FromResult<GeoPoint?>(null);
}
