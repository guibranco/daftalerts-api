using System;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DaftAlerts.Api.HostedServices;

public sealed class RetentionCleanupWorker : BackgroundService
{
    private static readonly TimeSpan RunAt = new(hours: 3, minutes: 0, seconds: 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RetentionOptions> _options;
    private readonly ILogger<RetentionCleanupWorker> _logger;

    public RetentionCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RetentionOptions> options,
        ILogger<RetentionCleanupWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun(DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionCleanupWorker iteration failed");
            }
        }
    }

    internal static TimeSpan ComputeDelayUntilNextRun(DateTime nowUtc)
    {
        var today = nowUtc.Date.Add(RunAt);
        var next = nowUtc < today ? today : today.AddDays(1);
        return next - nowUtc;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var rawEmails = scope.ServiceProvider.GetRequiredService<IRawEmailRepository>();
        var days = Math.Max(1, _options.CurrentValue.RawEmailDays);
        var threshold = DateTime.UtcNow.AddDays(-days);
        var deleted = await rawEmails.DeleteOlderThanAsync(threshold, ct);
        _logger.LogInformation(
            "RetentionCleanupWorker deleted {Deleted} raw emails older than {Days}d",
            deleted,
            days
        );
    }
}
