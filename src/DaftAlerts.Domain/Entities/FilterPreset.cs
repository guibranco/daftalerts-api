using System;
using System.Collections.Generic;

namespace DaftAlerts.Domain.Entities;

public sealed class FilterPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<string> RoutingKeys { get; set; } = Array.Empty<string>();

    public int? MinBeds { get; set; }
    public int? MaxBeds { get; set; }
    public int? MinBaths { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    public IReadOnlyList<string> PropertyTypes { get; set; } = Array.Empty<string>();

    public string? BerMin { get; set; }
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
