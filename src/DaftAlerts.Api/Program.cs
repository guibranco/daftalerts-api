using System;
using System.IO;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using DaftAlerts.Api.Configuration;
using DaftAlerts.Api.Endpoints;
using DaftAlerts.Api.Health;
using DaftAlerts.Api.HostedServices;
using DaftAlerts.Api.Middleware;
using DaftAlerts.Application.Options;
using DaftAlerts.Application.Validation;
using DaftAlerts.Infrastructure;
using DaftAlerts.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

namespace DaftAlerts.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- Serilog -------------------------------------------------------
        builder.Host.UseSerilog(
            (ctx, services, config) =>
            {
                config
                    .ReadFrom.Configuration(ctx.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId();
            }
        );

        // --- Options -------------------------------------------------------
        builder
            .Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
        builder
            .Services.AddOptions<CorsOptions>()
            .Bind(builder.Configuration.GetSection(CorsOptions.SectionName));

        // --- Infrastructure (DbContext, repos, parser, pipeline, geocoding)
        builder.Services.AddDaftAlertsInfrastructure(builder.Configuration);

        // --- Validation ----------------------------------------------------
        builder.Services.AddValidatorsFromAssemblyContaining<PropertyQueryValidator>();

        // --- CORS ----------------------------------------------------------
        builder.Services.AddCors(o =>
            o.AddDefaultPolicy(policy =>
            {
                var origins =
                    builder
                        .Configuration.GetSection(CorsOptions.SectionName + ":AllowedOrigins")
                        .Get<string[]>()
                    ?? Array.Empty<string>();
                if (origins.Length == 0)
                    policy.SetIsOriginAllowed(_ => false);
                else
                    policy.WithOrigins(origins);
                policy.AllowAnyHeader().AllowAnyMethod();
            })
        );

        // --- ProblemDetails + exception handler ---------------------------
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        // --- Rate limiting -------------------------------------------------
        builder.Services.AddApiRateLimiting(builder.Configuration);

        // --- Health checks -------------------------------------------------
        builder
            .Services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("sqlite")
            .AddCheck<GeocodingWorkerHealthCheck>("geocoding-worker", tags: new[] { "ready" });

        // --- Background workers -------------------------------------------
        builder.Services.AddHostedService<GeocodingWorker>();
        builder.Services.AddHostedService<RetentionCleanupWorker>();
        builder.Services.AddHostedService<ParseRetryWorker>();

        // --- Forwarded headers (for nginx) ---------------------------------
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        // --- Swagger (dev only) -------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(
                (document, _, _) =>
                {
                    document.Info = new()
                    {
                        Title = "DaftAlerts API",
                        Version = "v1",
                        Description = "Personal Daft.ie property aggregator API",
                    };
                    return Task.CompletedTask;
                }
            );
        });

        var app = builder.Build();

        // --- Pipeline ------------------------------------------------------
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.MapOpenApi();
        app.MapScalarApiReference();
        app.UseCors();
        app.UseIpRateLimiting();
        app.UseMiddleware<BearerTokenMiddleware>();

        // --- Endpoints -----------------------------------------------------
        app.MapPropertiesEndpoints();
        app.MapStatsEndpoints();
        app.MapPresetsEndpoints();

        app.MapHealthChecks("/health");
        app.MapHealthChecks(
            "/health/ready",
            new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = reg => reg.Tags.Contains("ready"),
            }
        );

        // --- DB: migrate + seed -------------------------------------------
        EnsureDatabaseDirectoryExists(app);
        await InitializeDatabaseAsync(app);

        await app.RunAsync();
    }

    static void EnsureDatabaseDirectoryExists(WebApplication app)
    {
        var connectionString =
            app.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default not configured");

        var csBuilder = new SqliteConnectionStringBuilder(connectionString);
        var dbPath = csBuilder.DataSource;

        if (!Path.IsPathRooted(dbPath))
            dbPath = Path.GetFullPath(dbPath, app.Environment.ContentRootPath);

        var directory = Path.GetDirectoryName(dbPath);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            app.Logger.LogWarning(
                ex,
                "Could not create database directory {Directory}; assuming it's managed externally",
                directory
            );
        }
    }

    private static async Task InitializeDatabaseAsync(WebApplication app)
    {
        var dbOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (dbOptions.AutoMigrate)
        {
            logger.LogInformation("Applying pending EF Core migrations...");
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogInformation(
                "AutoMigrate=false; ensure migrations have been applied out-of-band."
            );
        }

        await DatabaseSeeder.SeedAsync(db, default);
    }
}
