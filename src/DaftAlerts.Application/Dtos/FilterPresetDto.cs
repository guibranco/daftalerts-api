using System;
using System.Collections.Generic;

namespace DaftAlerts.Application.Dtos;

public sealed record FilterPresetDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> RoutingKeys,
    int? MinBeds,
    int? MaxBeds,
    int? MinBaths,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<string> PropertyTypes,
    string? BerMin,
    bool IsDefault,
    DateTime CreatedAt);

public sealed record UpsertFilterPresetDto(
    string Name,
    IReadOnlyList<string> RoutingKeys,
    int? MinBeds,
    int? MaxBeds,
    int? MinBaths,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<string> PropertyTypes,
    string? BerMin,
    bool IsDefault);
