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

    private readonly SqliteConnection _conn;

    public DaftAlertsApiFactory()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Create schema on the shared connection once.
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
                        ["Auth:ApiToken"] = "test-token",
                        ["Geocoding:GoogleApiKey"] = "",
                    }
                );
            }
        );

        builder.ConfigureServices(services =>
        {
            // Remove the production DbContext registration and bind it to our shared in-memory SQLite connection.
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));

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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        return new AppDbContext(options);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        _conn.Dispose();
        // Best-effort cleanup of temp db files

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
