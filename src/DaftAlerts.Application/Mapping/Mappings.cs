using System;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Application.Mapping;

public static class PropertyMappings
{
    public static PropertyDto ToDto(this Property p) => new(
        p.Id,
        p.DaftId,
        p.DaftUrl,
        p.Address,
        p.Eircode,
        p.RoutingKey,
        p.PriceMonthly,
        p.Currency,
        p.Beds,
        p.Baths,
        p.PropertyType,
        p.BerRating,
        p.MainImageUrl,
        p.Latitude,
        p.Longitude,
        p.Status.ToStatusString(),
        p.ReceivedAt,
        p.ApprovedAt,
        p.RecycledAt,
        p.Notes);

    public static string ToStatusString(this PropertyStatus s) => s switch
    {
        PropertyStatus.Inbox => "inbox",
        PropertyStatus.Approved => "approved",
        PropertyStatus.Recycled => "recycled",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    public static PropertyStatus ParseStatusString(string? s) => (s ?? string.Empty).ToLowerInvariant() switch
    {
        "inbox" => PropertyStatus.Inbox,
        "approved" => PropertyStatus.Approved,
        "recycled" => PropertyStatus.Recycled,
        _ => throw new ArgumentException($"Unknown status '{s}'.", nameof(s))
    };
}

public static class FilterPresetMappings
{
    public static FilterPresetDto ToDto(this FilterPreset f) => new(
        f.Id, f.Name, f.RoutingKeys,
        f.MinBeds, f.MaxBeds, f.MinBaths,
        f.MinPrice, f.MaxPrice,
        f.PropertyTypes, f.BerMin, f.IsDefault, f.CreatedAt);

    public static FilterPreset ApplyFrom(this FilterPreset entity, UpsertFilterPresetDto dto)
    {
        entity.Name = dto.Name;
        entity.RoutingKeys = dto.RoutingKeys;
        entity.MinBeds = dto.MinBeds;
        entity.MaxBeds = dto.MaxBeds;
        entity.MinBaths = dto.MinBaths;
        entity.MinPrice = dto.MinPrice;
        entity.MaxPrice = dto.MaxPrice;
        entity.PropertyTypes = dto.PropertyTypes;
        entity.BerMin = dto.BerMin;
        entity.IsDefault = dto.IsDefault;
        return entity;
    }
}
