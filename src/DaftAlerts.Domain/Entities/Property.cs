using System;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Domain.Entities;

public sealed class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DaftId { get; set; } = string.Empty;
    public string DaftUrl { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Eircode { get; set; }
    public string? RoutingKey { get; set; }

    public decimal PriceMonthly { get; set; }
    public string Currency { get; set; } = "EUR";

    public int Beds { get; set; }
    public int Baths { get; set; }
    public string PropertyType { get; set; } = "Other";

    public string? BerRating { get; set; }
    public string? MainImageUrl { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public PropertyStatus Status { get; set; } = PropertyStatus.Inbox;

    public DateTime ReceivedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RecycledAt { get; set; }

    public string? Notes { get; set; }

    public string RawSubject { get; set; } = string.Empty;
    public string? RawEmailMessageId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
