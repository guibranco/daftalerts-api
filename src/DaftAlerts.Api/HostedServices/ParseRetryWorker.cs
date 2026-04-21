using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DaftAlerts.Api.HostedServices;

public sealed class ParseRetryWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinAge = TimeSpan.FromHours(1);
    private const int BatchSize = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParseRetryWorker> _logger;

    public ParseRetryWorker(IServiceScopeFactory scopeFactory, ILogger<ParseRetryWorker> logger)
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
                _logger.LogError(ex, "ParseRetryWorker iteration failed");
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var rawEmails = scope.ServiceProvider.GetRequiredService<IRawEmailRepository>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IEmailIngestionPipeline>();

        var pending = await rawEmails.GetFailedForRetryAsync(MinAge, BatchSize, ct);
        foreach (var email in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = new MemoryStream(email.RawMimeBytes, writable: false);
                var result = await pipeline.IngestAsync(stream, ct);
                _logger.LogInformation("Retry for raw email id={Id} -> {Outcome}", email.Id, result.Outcome);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry failed for raw email id={Id}", email.Id);
            }
        }
    }
}
