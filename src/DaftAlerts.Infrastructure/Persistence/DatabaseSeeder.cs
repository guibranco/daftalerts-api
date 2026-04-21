using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DaftAlerts.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.FilterPresets.AnyAsync(ct))
        {
            db.FilterPresets.Add(new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "Dublin central rentals",
                RoutingKeys = new[] { "D01", "D02", "D04", "D06", "D08" },
                MinBeds = 1,
                MaxBeds = 3,
                MinBaths = 1,
                MaxPrice = 3500m,
                PropertyTypes = new[] { "House", "Apartment" },
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
