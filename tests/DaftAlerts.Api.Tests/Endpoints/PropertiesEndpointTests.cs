using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DaftAlerts.Api.Tests.Infra;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Api.Tests.Endpoints;

public sealed class PropertiesEndpointTests : IClassFixture<DaftAlertsApiFactory>
{
    private readonly DaftAlertsApiFactory _factory;

    public PropertiesEndpointTests(DaftAlertsApiFactory factory)
    {
        _factory = factory;
        SeedOnce();
    }

    private static int _seeded;

    private void SeedOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _seeded, 1) == 1)
            return;

        using var db = _factory.CreateDbContext();
        db.Properties.AddRange(
            NewProperty(
                "100",
                "D02",
                PropertyStatus.Inbox,
                2000,
                1,
                "A1",
                "Apartment",
                DateTime.UtcNow.AddDays(-1)
            ),
            NewProperty(
                "101",
                "D02",
                PropertyStatus.Inbox,
                3500,
                3,
                "C3",
                "House",
                DateTime.UtcNow.AddDays(-2)
            ),
            NewProperty(
                "102",
                "D04",
                PropertyStatus.Inbox,
                1500,
                1,
                "G",
                "Studio",
                DateTime.UtcNow.AddDays(-3)
            ),
            NewProperty(
                "200",
                "D08",
                PropertyStatus.Approved,
                1800,
                2,
                "B2",
                "Apartment",
                DateTime.UtcNow.AddDays(-4)
            ),
            NewProperty(
                "300",
                "D06",
                PropertyStatus.Recycled,
                2500,
                2,
                "B1",
                "House",
                DateTime.UtcNow.AddDays(-5)
            )
        );
        db.SaveChanges();
    }

    private static Property NewProperty(
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

    [Fact]
    public async Task List_without_auth_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/properties?status=inbox");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_with_bad_token_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "wrong-token"
        );
        var resp = await client.GetAsync("/api/properties?status=inbox");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_with_auth_returns_inbox()
    {
        var client = _factory.CreateAuthedClient();
        var page = await client.GetFromJsonAsync<PagedResult<PropertyDto>>(
            "/api/properties?status=inbox"
        );
        page.Should().NotBeNull();
        page!.Total.Should().BeGreaterThanOrEqualTo(3);
        page.Items.Should().OnlyContain(p => p.Status == "inbox");
    }

    [Fact]
    public async Task List_filters_by_routing_keys()
    {
        var client = _factory.CreateAuthedClient();
        var page = await client.GetFromJsonAsync<PagedResult<PropertyDto>>(
            "/api/properties?status=inbox&routingKeys=D02"
        );
        page!.Items.Should().OnlyContain(p => p.RoutingKey == "D02");
        page.Items.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task List_validation_error_returns_400()
    {
        var client = _factory.CreateAuthedClient();
        var resp = await client.GetAsync("/api/properties?status=inbox&berMin=H1");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_by_id_404_for_unknown()
    {
        var client = _factory.CreateAuthedClient();
        var resp = await client.GetAsync($"/api/properties/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_approves_and_sets_ApprovedAt()
    {
        var client = _factory.CreateAuthedClient();
        using var db = _factory.CreateDbContext();
        var target = db.Properties.First(p => p.DaftId == "101");

        var resp = await client.PatchAsJsonAsync(
            $"/api/properties/{target.Id}",
            new UpdatePropertyDto("approved", "great location")
        );
        resp.EnsureSuccessStatusCode();

        var updated = await resp.Content.ReadFromJsonAsync<PropertyDto>();
        updated!.Status.Should().Be("approved");
        updated.ApprovedAt.Should().NotBeNull();
        updated.Notes.Should().Be("great location");
    }

    [Fact]
    public async Task Patch_validation_error_returns_400()
    {
        var client = _factory.CreateAuthedClient();
        using var db = _factory.CreateDbContext();
        var target = db.Properties.First();

        var resp = await client.PatchAsJsonAsync(
            $"/api/properties/{target.Id}",
            new UpdatePropertyDto("archived", null)
        );
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bulk_recycle_updates_count()
    {
        var client = _factory.CreateAuthedClient();
        using var db = _factory.CreateDbContext();
        var ids = db
            .Properties.Where(p => p.Status == PropertyStatus.Inbox)
            .Select(p => p.Id)
            .Take(2)
            .ToList();

        var resp = await client.PostAsJsonAsync(
            "/api/properties/bulk",
            new BulkActionDto(ids, "recycle")
        );
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<BulkActionResultDto>();
        result!.Updated.Should().Be(ids.Count);
    }

    [Fact]
    public async Task Stats_returns_counts()
    {
        var client = _factory.CreateAuthedClient();
        var stats = await client.GetFromJsonAsync<StatsDto>("/api/stats");
        stats.Should().NotBeNull();
        (stats!.InboxCount + stats.ApprovedCount + stats.RecycledCount).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Health_liveness_is_open()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
