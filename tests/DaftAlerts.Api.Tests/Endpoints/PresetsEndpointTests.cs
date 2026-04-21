using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DaftAlerts.Api.Tests.Infra;
using DaftAlerts.Application.Dtos;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Api.Tests.Endpoints;

public sealed class PresetsEndpointTests : IClassFixture<DaftAlertsApiFactory>
{
    private readonly DaftAlertsApiFactory _factory;

    public PresetsEndpointTests(DaftAlertsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crud_flow_works()
    {
        var client = _factory.CreateAuthedClient();

        var createResp = await client.PostAsJsonAsync("/api/presets", new UpsertFilterPresetDto(
            Name: "Test preset",
            RoutingKeys: new[] { "D02", "D04" },
            MinBeds: 1, MaxBeds: 3, MinBaths: 1,
            MinPrice: null, MaxPrice: 3000m,
            PropertyTypes: new[] { "Apartment" },
            BerMin: "C3", IsDefault: false));

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<FilterPresetDto>();
        created.Should().NotBeNull();

        var updateResp = await client.PutAsJsonAsync($"/api/presets/{created!.Id}", new UpsertFilterPresetDto(
            Name: "Renamed",
            RoutingKeys: new[] { "D02" },
            MinBeds: 2, MaxBeds: 3, MinBaths: 1,
            MinPrice: null, MaxPrice: 3000m,
            PropertyTypes: new[] { "Apartment" },
            BerMin: "B1", IsDefault: false));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResp = await client.DeleteAsync($"/api/presets/{created.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await client.DeleteAsync($"/api/presets/{created.Id}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_with_invalid_routing_key_returns_400()
    {
        var client = _factory.CreateAuthedClient();
        var resp = await client.PostAsJsonAsync("/api/presets", new UpsertFilterPresetDto(
            Name: "Bad",
            RoutingKeys: new[] { "ZZZ" },
            MinBeds: null, MaxBeds: null, MinBaths: null,
            MinPrice: null, MaxPrice: null,
            PropertyTypes: System.Array.Empty<string>(),
            BerMin: null, IsDefault: false));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
