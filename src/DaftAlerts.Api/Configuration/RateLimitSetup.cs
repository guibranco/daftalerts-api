using AspNetCoreRateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DaftAlerts.Api.Configuration;

internal static class RateLimitSetup
{
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services.AddMemoryCache();

        services.Configure<IpRateLimitOptions>(opts =>
        {
            opts.EnableEndpointRateLimiting = true;
            opts.StackBlockedRequests = false;
            opts.HttpStatusCode = 429;
            opts.RealIpHeader = "X-Forwarded-For";
            opts.ClientIdHeader = "X-ClientId";
            opts.GeneralRules =
            [
                new RateLimitRule
                {
                    Endpoint = "*:/api/*",
                    Period = "1m",
                    Limit = 300,
                },
            ];
        });

        services.AddInMemoryRateLimiting();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        return services;
    }
}
