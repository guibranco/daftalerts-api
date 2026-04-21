using System;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DaftAlerts.Api.HostedServices;

public sealed class GeocodingWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeocodingWorker> _logger;

    public static DateTime LastRunUtc { get; private set; } = DateTime.MinValue;

    public GeocodingWorker(IServiceScopeFactory scopeFactory, ILogger<GeocodingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeocodingWorker iteration failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyRepository>();
        var geocoder = scope.ServiceProvider.GetRequiredService<IGeocodingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pending = await properties.GetPendingGeocodeAsync(BatchSize, ct);
        if (pending.Count == 0)
        {
            LastRunUtc = DateTime.UtcNow;
            return;
        }

        foreach (var p in pending)
        {
            ct.ThrowIfCancellationRequested();
            var point = await geocoder.GeocodeAsync(p.Address, p.Eircode, ct);
            if (point is null) continue;
            p.Latitude = point.Value.Latitude;
            p.Longitude = point.Value.Longitude;
            p.UpdatedAt = DateTime.UtcNow;
        }

        await uow.SaveChangesAsync(ct);
        LastRunUtc = DateTime.UtcNow;
        _logger.LogInformation("GeocodingWorker processed {Count} properties", pending.Count);
    }
}
