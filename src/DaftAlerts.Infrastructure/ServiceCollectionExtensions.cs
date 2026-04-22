using System;
using System.Net;
using System.Net.Http;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Options;
using DaftAlerts.Application.Parsing;
using DaftAlerts.Infrastructure.Geocoding;
using DaftAlerts.Infrastructure.Ingestion;
using DaftAlerts.Infrastructure.Parsing;
using DaftAlerts.Infrastructure.Persistence;
using DaftAlerts.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace DaftAlerts.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDaftAlertsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<GeocodingOptions>()
            .Bind(configuration.GetSection(GeocodingOptions.SectionName));
        services
            .AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName));
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();

        var connectionString =
            configuration.GetConnectionString("Default")
            ?? "Data Source=./data/daftalerts.db;Cache=Shared;Foreign Keys=true";

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IRawEmailRepository, RawEmailRepository>();
        services.AddScoped<IFilterPresetRepository, FilterPresetRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<IDaftEmailParser, DaftEmailParser>();
        services.AddScoped<IEmailIngestionPipeline, EmailIngestionPipeline>();

        AddGeocodingHttpClients(services);

        services.AddScoped<IGeocodingService, HybridGeocodingService>();

        return services;
    }

    private static void AddGeocodingHttpClients(IServiceCollection services)
    {
        services
            .AddHttpClient<GoogleGeocoder>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetTimeoutPolicy());

        services
            .AddHttpClient<NominatimGeocoder>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetTimeoutPolicy());
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
        Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
}
