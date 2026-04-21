using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Infrastructure;
using DaftAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DaftAlerts.EmailIngest;

public static class Program
{
    /// <summary>
    /// Postfix pipes a MIME message to stdin. We read it all, persist + parse,
    /// and always exit 0 unless something catastrophic (e.g. DB unreachable) happened.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        // Short deadline — Postfix expects the command to return quickly.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(
                (ctx, cfg) =>
                {
                    cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    cfg.AddJsonFile(
                        $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json",
                        optional: true,
                        reloadOnChange: false
                    );
                    cfg.AddEnvironmentVariables(prefix: "DaftAlerts__");
                    cfg.AddCommandLine(args);
                }
            )
            .UseSerilog(
                (ctx, _, config) =>
                {
                    config
                        .ReadFrom.Configuration(ctx.Configuration)
                        .Enrich.FromLogContext()
                        .Enrich.WithMachineName()
                        .Enrich.WithThreadId();
                }
            )
            .ConfigureServices(
                (ctx, services) =>
                {
                    services.AddDaftAlertsInfrastructure(ctx.Configuration);
                }
            )
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<IEmailIngestionPipeline>>();

        try
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cts.Token);

            var pipeline = scope.ServiceProvider.GetRequiredService<IEmailIngestionPipeline>();

            // Read stdin into memory so MimeKit can random-access it.
            using var stdin = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            await stdin.CopyToAsync(buffer, cts.Token);
            buffer.Position = 0;

            if (buffer.Length == 0)
            {
                logger.LogWarning("Ingest: empty stdin, nothing to do.");
                return 0;
            }

            var result = await pipeline.IngestAsync(buffer, cts.Token);
            logger.LogInformation(
                "Ingest finished: outcome={Outcome}, messageId={MessageId}, propertyId={PropertyId}, rawEmailId={RawEmailId}",
                result.Outcome,
                result.MessageId,
                result.PropertyId,
                result.RawEmailId
            );

            return 0;
        }
        catch (Exception ex)
        {
            // Never bounce the mail — log and swallow.
            logger.LogError(ex, "Ingest failed with unhandled exception.");
            return 0;
        }
        finally
        {
            await host.StopAsync(TimeSpan.FromSeconds(2));
            host.Dispose();
            await Log.CloseAndFlushAsync();
        }
    }
}
