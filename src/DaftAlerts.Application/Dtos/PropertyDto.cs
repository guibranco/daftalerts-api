using System;

namespace DaftAlerts.Application.Dtos;

public sealed record PropertyDto(
    Guid Id,
    string DaftId,
    string DaftUrl,
    string Address,
    string? Eircode,
    string? RoutingKey,
    decimal PriceMonthly,
    string Currency,
    int Beds,
    int Baths,
    string PropertyType,
    string? BerRating,
    string? MainImageUrl,
    double? Latitude,
    double? Longitude,
    string Status,
    DateTime ReceivedAt,
    DateTime? ApprovedAt,
    DateTime? RecycledAt,
    string? Notes
);
