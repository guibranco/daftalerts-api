using DaftAlerts.Application.Dtos;
using DaftAlerts.Application.Validation;
using DaftAlerts.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Application.Tests.Validation;

public sealed class ValidatorTests
{
    private static PropertyQuery BaseQuery(params (string name, object? value)[] overrides) =>
        new(
            Status: PropertyStatus.Inbox,
            Page: 1,
            PageSize: 24,
            Search: null,
            RoutingKeys: null,
            MinBeds: null,
            MaxBeds: null,
            MinBaths: null,
            MinPrice: null,
            MaxPrice: null,
            PropertyTypes: null,
            BerMin: null,
            SortBy: PropertySortField.ReceivedAt,
            SortDir: SortDirection.Desc
        );

    [Fact]
    public void PropertyQuery_valid_passes()
    {
        new PropertyQueryValidator().Validate(BaseQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PropertyQuery_invalid_routing_keys_fails()
    {
        var q = BaseQuery() with { RoutingKeys = new[] { "ZZ9" } };
        new PropertyQueryValidator().Validate(q).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PropertyQuery_min_greater_than_max_fails()
    {
        var q = BaseQuery() with { MinBeds = 5, MaxBeds = 2 };
        new PropertyQueryValidator().Validate(q).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PropertyQuery_invalid_bermin_fails()
    {
        var q = BaseQuery() with { BerMin = "H1" };
        new PropertyQueryValidator().Validate(q).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdatePropertyDto_invalid_status_fails()
    {
        new UpdatePropertyValidator()
            .Validate(new UpdatePropertyDto("archived", null))
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void BulkAction_empty_ids_fails()
    {
        new BulkActionValidator()
            .Validate(new BulkActionDto(System.Array.Empty<System.Guid>(), "approve"))
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void BulkAction_bad_action_fails()
    {
        new BulkActionValidator()
            .Validate(new BulkActionDto(new[] { System.Guid.NewGuid() }, "delete"))
            .IsValid.Should()
            .BeFalse();
    }
}
