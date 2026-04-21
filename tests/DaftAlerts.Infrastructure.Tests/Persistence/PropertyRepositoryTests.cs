using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using DaftAlerts.Infrastructure.Persistence;
using DaftAlerts.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DaftAlerts.Infrastructure.Tests.Persistence;

public sealed class PropertyRepositoryTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private AppDbContext _db = null!;
    private PropertyRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;

        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _repo = new PropertyRepository(_db);

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        _db.Properties.AddRange(
            Make("1", "D02", PropertyStatus.Inbox, 2000, 1, "A1", "Apartment", now.AddDays(-1)),
            Make("2", "D02", PropertyStatus.Inbox, 3500, 3, "C3", "House", now.AddDays(-2)),
            Make("3", "D04", PropertyStatus.Inbox, 1500, 1, "G", "Studio", now.AddDays(-3)),
            Make("4", "D08", PropertyStatus.Approved, 1800, 2, null, "Apartment", now.AddDays(-4)),
            Make("5", "D06", PropertyStatus.Recycled, 2500, 2, "B2", "House", now.AddDays(-5))
        );
        await _db.SaveChangesAsync();
    }

    private static Property Make(
        string id,
        string rk,
        PropertyStatus status,
        decimal price,
        int beds,
        string? ber,
        string type,
        DateTime receivedAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            DaftId = id,
            DaftUrl = $"https://www.daft.ie/for-rent/x/{id}",
            Address = $"Address {id}, {rk}",
            RoutingKey = rk,
            PriceMonthly = price,
            Beds = beds,
            Baths = 1,
            PropertyType = type,
            BerRating = ber,
            Status = status,
            ReceivedAt = receivedAt,
            RawSubject = $"subj-{id}",
        };

    private static PropertyQuery Query(PropertyStatus status, Action<QueryBuilder>? mutate = null)
    {
        var b = new QueryBuilder(status);
        mutate?.Invoke(b);
        return b.Build();
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var r = await _repo.QueryAsync(Query(PropertyStatus.Inbox), CancellationToken.None);
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task Filters_by_routing_keys()
    {
        var r = await _repo.QueryAsync(
            Query(PropertyStatus.Inbox, q => q.RoutingKeys = new[] { "D02" }),
            CancellationToken.None
        );
        r.Total.Should().Be(2);
    }

    [Fact]
    public async Task Filters_by_min_beds()
    {
        var r = await _repo.QueryAsync(
            Query(PropertyStatus.Inbox, q => q.MinBeds = 2),
            CancellationToken.None
        );
        r.Total.Should().Be(1);
    }

    [Fact]
    public async Task Filters_by_price_range()
    {
        var r = await _repo.QueryAsync(
            Query(
                PropertyStatus.Inbox,
                q =>
                {
                    q.MinPrice = 1800m;
                    q.MaxPrice = 3000m;
                }
            ),
            CancellationToken.None
        );
        r.Total.Should().Be(1);
    }

    [Fact]
    public async Task Filters_by_berMin_using_scalar_function()
    {
        using var ctx = TestDbContextFactory.Create(out var conn);
        // berMin=C3 means C3 or better (rank <= 9). Inbox has A1(1), C3(9), G(15). Expect A1 and C3.
        var r = await _repo.QueryAsync(
            Query(PropertyStatus.Inbox, q => q.BerMin = "C3"),
            CancellationToken.None
        );
        r.Total.Should().Be(2);
        conn.Dispose();
    }

    [Fact]
    public async Task Filters_by_search_on_address()
    {
        var r = await _repo.QueryAsync(
            Query(PropertyStatus.Inbox, q => q.Search = "D04"),
            CancellationToken.None
        );
        r.Total.Should().Be(1);
    }

    [Fact]
    public async Task Sorts_by_price_asc()
    {
        var r = await _repo.QueryAsync(
            Query(
                PropertyStatus.Inbox,
                q =>
                {
                    q.SortBy = PropertySortField.Price;
                    q.SortDir = SortDirection.Asc;
                }
            ),
            CancellationToken.None
        );
        r.Items[0].PriceMonthly.Should().Be(1500m);
        r.Items[^1].PriceMonthly.Should().Be(3500m);
    }

    [Fact]
    public async Task Pages_results()
    {
        var r = await _repo.QueryAsync(
            Query(
                PropertyStatus.Inbox,
                q =>
                {
                    q.Page = 1;
                    q.PageSize = 2;
                }
            ),
            CancellationToken.None
        );
        r.Items.Should().HaveCount(2);
        r.Total.Should().Be(3);
        r.Page.Should().Be(1);
        r.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetStats_computes_avg_and_median()
    {
        var more = DateTime.UtcNow.AddDays(-10);
        _db.Properties.Add(
            Make("6", "D02", PropertyStatus.Approved, 2000, 2, "B1", "Apartment", more)
        );
        _db.Properties.Add(
            Make("7", "D02", PropertyStatus.Approved, 2200, 2, "B1", "Apartment", more)
        );
        await _db.SaveChangesAsync();

        var stats = await _repo.GetStatsAsync(CancellationToken.None);
        stats.ApprovedCount.Should().Be(3);
        // approved prices are 1800, 2000, 2200 -> avg 2000, median 2000
        stats.AvgApprovedPrice.Should().Be(2000m);
        stats.MedianApprovedPrice.Should().Be(2000m);
    }

    private sealed class QueryBuilder
    {
        public PropertyStatus Status { get; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;
        public string? Search { get; set; }
        public IReadOnlyList<string>? RoutingKeys { get; set; }
        public int? MinBeds { get; set; }
        public int? MaxBeds { get; set; }
        public int? MinBaths { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public IReadOnlyList<string>? PropertyTypes { get; set; }
        public string? BerMin { get; set; }
        public PropertySortField SortBy { get; set; } = PropertySortField.ReceivedAt;
        public SortDirection SortDir { get; set; } = SortDirection.Desc;

        public QueryBuilder(PropertyStatus status)
        {
            Status = status;
        }

        public PropertyQuery Build() =>
            new(
                Status,
                Page,
                PageSize,
                Search,
                RoutingKeys,
                MinBeds,
                MaxBeds,
                MinBaths,
                MinPrice,
                MaxPrice,
                PropertyTypes,
                BerMin,
                SortBy,
                SortDir
            );
    }
}
