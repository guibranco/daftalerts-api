using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DaftAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DaftAlerts.EmailIngest.Tests;

/// <summary>
/// Spawns the published <c>DaftAlerts.EmailIngest</c> binary as a process, pipes a sample .eml to stdin,
/// and asserts the resulting DB state.
/// </summary>
public sealed class EmailIngestEndToEndTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _samplesDir;

    public EmailIngestEndToEndTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daftalerts-test-{Guid.NewGuid():N}.db");
        _samplesDir = Path.Combine(
            Path.GetDirectoryName(typeof(EmailIngestEndToEndTests).Assembly.Location)!,
            "TestData"
        );
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private string FindIngestBinary()
    {
        // Walk from the test output dir up to the repo root, then down into the ingest bin.
        var dir = Path.GetDirectoryName(typeof(EmailIngestEndToEndTests).Assembly.Location)!;
        var repoRoot = dir;
        for (var i = 0; i < 10 && !Directory.Exists(Path.Combine(repoRoot, "src")); i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;

        var ingestProject = Path.Combine(repoRoot, "src", "DaftAlerts.EmailIngest");
        // Run via `dotnet run` so we don't need a separate publish step.
        return ingestProject;
    }

    [Fact(
        Skip = "Requires the EmailIngest project to be buildable in the test environment; run manually with `dotnet test --filter EndToEnd`."
    )]
    public async Task Pipes_sample_eml_and_creates_property()
    {
        var ingestProjectDir = FindIngestBinary();
        var samplePath = Path.Combine(_samplesDir, "sample-daft-herbert-lane.eml");

        if (!File.Exists(samplePath))
            throw new FileNotFoundException(
                "Sample .eml is missing; ensure TestData is copied to test output.",
                samplePath
            );

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = ingestProjectDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(ingestProjectDir);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--environment=Testing");

        psi.EnvironmentVariables["DaftAlerts__ConnectionStrings__Default"] =
            $"Data Source={_dbPath};Cache=Shared;Foreign Keys=true";
        psi.EnvironmentVariables["DaftAlerts__Database__AutoMigrate"] = "true";

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        await using (var stdin = process.StandardInput.BaseStream)
        await using (var sample = File.OpenRead(samplePath))
        {
            await sample.CopyToAsync(stdin);
        }

        var completed = process.WaitForExit(milliseconds: 60_000);
        completed.Should().BeTrue("the ingest process should complete within 60s");
        process.ExitCode.Should().Be(0);

        // Assert DB state.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        await using var db = new AppDbContext(options);
        var property = await db.Properties.FirstOrDefaultAsync(p => p.DaftId == "6546017");
        property.Should().NotBeNull();
        property!.Eircode.Should().Be("D02KC86");
        property.PriceMonthly.Should().Be(2850m);
    }
}
