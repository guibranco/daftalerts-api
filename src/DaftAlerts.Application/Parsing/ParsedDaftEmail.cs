using System;

namespace DaftAlerts.Application.Parsing;

/// <summary>
/// Result of successfully parsing a Daft.ie property-alert email.
/// </summary>
public sealed record ParsedDaftEmail(
    string DaftId,
    string DaftUrl,
    string Address,
    string? Eircode,
    string? RoutingKey,
    decimal PriceMonthly,
    int Beds,
    int Baths,
    string PropertyType,
    string? BerRating,
    string? MainImageUrl,
    DateTime ReceivedAt,
    string RawSubject,
    string? MessageId);
