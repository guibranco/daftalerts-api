using System;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Api.HostedServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DaftAlerts.Api.Health;

/// <summary>Healthy if the geocoding worker has run in the last 5 minutes, or if no work is expected yet.</summary>
public sealed class GeocodingWorkerHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var last = GeocodingWorker.LastRunUtc;
        if (last == DateTime.MinValue)
            return Task.FromResult(HealthCheckResult.Healthy("Worker has not run yet; will run within 60s of startup."));

        var age = DateTime.UtcNow - last;
        return Task.FromResult(age <= TimeSpan.FromMinutes(5)
            ? HealthCheckResult.Healthy($"Last run {age.TotalSeconds:F0}s ago.")
            : HealthCheckResult.Degraded($"Last run {age.TotalMinutes:F1} minutes ago."));
    }
}
