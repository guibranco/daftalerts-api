using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using DaftAlerts.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DaftAlerts.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<FilterPreset> FilterPresets => Set<FilterPreset>();
    public DbSet<RawEmail> RawEmails => Set<RawEmail>();
    public DbSet<GeocodeCache> GeocodeCaches => Set<GeocodeCache>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new SqliteScalarFunctionInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Map the berrank(...) scalar function so LINQ calls to SqliteFunctions.BerRank translate to SQL.
        b.HasDbFunction(typeof(Repositories.SqliteFunctions).GetMethod(nameof(Repositories.SqliteFunctions.BerRank))!)
            .HasName("berrank");

        ConfigureProperty(b.Entity<Property>());
        ConfigureFilterPreset(b.Entity<FilterPreset>());
        ConfigureRawEmail(b.Entity<RawEmail>());
        ConfigureGeocodeCache(b.Entity<GeocodeCache>());
    }

    private static void ConfigureProperty(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Property> b)
    {
        b.ToTable("Properties");
        b.HasKey(p => p.Id);

        b.Property(p => p.DaftId).IsRequired().HasMaxLength(64);
        b.HasIndex(p => p.DaftId).IsUnique();

        b.Property(p => p.DaftUrl).IsRequired().HasMaxLength(512);
        b.Property(p => p.Address).IsRequired().HasMaxLength(512);
        b.Property(p => p.Eircode).HasMaxLength(10);
        b.Property(p => p.RoutingKey).HasMaxLength(3);

        b.Property(p => p.PriceMonthly).HasColumnType("DECIMAL(10,2)");
        b.Property(p => p.Currency).IsRequired().HasMaxLength(3);

        b.Property(p => p.PropertyType).IsRequired().HasMaxLength(32);
        b.Property(p => p.BerRating).HasMaxLength(8);
        b.Property(p => p.MainImageUrl).HasMaxLength(1024);
        b.Property(p => p.RawSubject).IsRequired().HasMaxLength(512);
        b.Property(p => p.RawEmailMessageId).HasMaxLength(256);
        b.Property(p => p.Notes).HasMaxLength(4000);

        b.Property(p => p.Status).HasConversion<int>();

        b.HasIndex(p => p.Status);
        b.HasIndex(p => p.RoutingKey);
        b.HasIndex(p => p.ReceivedAt).IsDescending();
        b.HasIndex(p => new { p.Status, p.ReceivedAt }).IsDescending(false, true);
    }

    private static void ConfigureFilterPreset(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<FilterPreset> b)
    {
        b.ToTable("FilterPresets");
        b.HasKey(f => f.Id);

        b.Property(f => f.Name).IsRequired().HasMaxLength(200);

        var stringListConverter = new ValueConverter<IReadOnlyList<string>, string>(
            v => JsonSerializer.Serialize(v, JsonContext.Options),
            v => JsonSerializer.Deserialize<IReadOnlyList<string>>(v, JsonContext.Options) ?? Array.Empty<string>());

        var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, c) => (a ?? Array.Empty<string>()).SequenceEqual(c ?? Array.Empty<string>()),
            v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
            v => v == null ? Array.Empty<string>() : v.ToArray());

        b.Property(f => f.RoutingKeys)
            .HasColumnName("RoutingKeysJson")
            .HasColumnType("TEXT")
            .HasConversion(stringListConverter, stringListComparer);

        b.Property(f => f.PropertyTypes)
            .HasColumnName("PropertyTypesJson")
            .HasColumnType("TEXT")
            .HasConversion(stringListConverter, stringListComparer);

        b.Property(f => f.MinPrice).HasColumnType("DECIMAL(10,2)");
        b.Property(f => f.MaxPrice).HasColumnType("DECIMAL(10,2)");
        b.Property(f => f.BerMin).HasMaxLength(8);
    }

    private static void ConfigureRawEmail(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RawEmail> b)
    {
        b.ToTable("RawEmails");
        b.HasKey(r => r.Id);
        b.Property(r => r.MessageId).IsRequired().HasMaxLength(256);
        b.HasIndex(r => r.MessageId).IsUnique();
        b.Property(r => r.Subject).HasMaxLength(512);
        b.Property(r => r.ParseStatus).HasConversion<int>();
        b.Property(r => r.ParseError).HasMaxLength(4000);
        b.HasIndex(r => r.ParseStatus);
        b.HasIndex(r => r.ReceivedAt);
    }

    private static void ConfigureGeocodeCache(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<GeocodeCache> b)
    {
        b.ToTable("GeocodeCache");
        b.HasKey(g => g.Key);
        b.Property(g => g.Key).HasMaxLength(512);
        b.Property(g => g.Provider).HasMaxLength(32);
        b.HasIndex(g => g.ExpiresAt);
    }
}

file static class JsonContext
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

/// <summary>
/// Registers the <c>berrank</c> SQLite scalar function on every new connection.
/// Maps BER strings ("A1".."G","Exempt", NULL) to an integer ordinal so EF can use it in WHERE clauses.
/// </summary>
internal sealed class SqliteScalarFunctionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RegisterFunctions(connection);
    }

    public override System.Threading.Tasks.Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        System.Threading.CancellationToken cancellationToken = default)
    {
        RegisterFunctions(connection);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private static void RegisterFunctions(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite) return;

        sqlite.CreateFunction<string?, int>(
            name: "berrank",
            function: (string? ber) => BerRank.Rank(ber),
            isDeterministic: true);
    }
}
