using System;
using System.Collections.Generic;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Application.Dtos;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record PropertyQuery(
    PropertyStatus Status,
    int Page,
    int PageSize,
    string? Search,
    IReadOnlyList<string>? RoutingKeys,
    int? MinBeds,
    int? MaxBeds,
    int? MinBaths,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<string>? PropertyTypes,
    string? BerMin,
    PropertySortField SortBy,
    SortDirection SortDir
);

public enum PropertySortField
{
    ReceivedAt,
    Price,
    Beds,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed record UpdatePropertyDto(string? Status, string? Notes);

public sealed record BulkActionDto(IReadOnlyList<Guid> Ids, string Action);

public sealed record BulkActionResultDto(int Updated);

public sealed record StatsDto(
    int InboxCount,
    int ApprovedCount,
    int RecycledCount,
    decimal AvgApprovedPrice,
    decimal MedianApprovedPrice
);
