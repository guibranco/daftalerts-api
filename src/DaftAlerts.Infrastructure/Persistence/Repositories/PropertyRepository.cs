using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using DaftAlerts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DaftAlerts.Infrastructure.Persistence.Repositories;

public sealed class PropertyRepository : IPropertyRepository
{
    private readonly AppDbContext _db;

    public PropertyRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Property?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Properties.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Property?> GetByDaftIdAsync(string daftId, CancellationToken ct) =>
        _db.Properties.FirstOrDefaultAsync(p => p.DaftId == daftId, ct);

    public async Task AddAsync(Property property, CancellationToken ct)
    {
        await _db.Properties.AddAsync(property, ct);
    }

    public async Task<PagedResult<Property>> QueryAsync(PropertyQuery query, CancellationToken ct)
    {
        IQueryable<Property> q = _db.Properties.AsNoTracking().Where(p => p.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.Address, $"%{s}%") ||
                (p.Notes != null && EF.Functions.Like(p.Notes, $"%{s}%")));
        }

        if (query.RoutingKeys is { Count: > 0 })
        {
            var rks = query.RoutingKeys.Select(r => r.ToUpperInvariant()).ToArray();
            q = q.Where(p => p.RoutingKey != null && rks.Contains(p.RoutingKey));
        }

        if (query.MinBeds.HasValue) q = q.Where(p => p.Beds >= query.MinBeds.Value);
        if (query.MaxBeds.HasValue) q = q.Where(p => p.Beds <= query.MaxBeds.Value);
        if (query.MinBaths.HasValue) q = q.Where(p => p.Baths >= query.MinBaths.Value);
        if (query.MinPrice.HasValue) q = q.Where(p => p.PriceMonthly >= query.MinPrice.Value);
        if (query.MaxPrice.HasValue) q = q.Where(p => p.PriceMonthly <= query.MaxPrice.Value);

        if (query.PropertyTypes is { Count: > 0 })
        {
            var types = query.PropertyTypes.ToArray();
            q = q.Where(p => types.Contains(p.PropertyType));
        }

        if (!string.IsNullOrWhiteSpace(query.BerMin))
        {
            var minRank = BerRank.Rank(query.BerMin);
            // Passing if: BER unknown (null/unknown) OR ranked <= minRank.
            // Using SQLite scalar function berrank(...).
            q = q.Where(p => p.BerRating == null || SqliteFunctions.BerRank(p.BerRating) <= minRank);
        }

        q = (query.SortBy, query.SortDir) switch
        {
            (PropertySortField.Price, SortDirection.Asc) => q.OrderBy(p => p.PriceMonthly),
            (PropertySortField.Price, SortDirection.Desc) => q.OrderByDescending(p => p.PriceMonthly),
            (PropertySortField.Beds, SortDirection.Asc) => q.OrderBy(p => p.Beds),
            (PropertySortField.Beds, SortDirection.Desc) => q.OrderByDescending(p => p.Beds),
            (_, SortDirection.Asc) => q.OrderBy(p => p.ReceivedAt),
            _ => q.OrderByDescending(p => p.ReceivedAt),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        return new PagedResult<Property>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<Property>> GetPendingGeocodeAsync(int batchSize, CancellationToken ct)
    {
        return await _db.Properties
            .Where(p => p.Latitude == null)
            .OrderBy(p => p.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<int> UpdateStatusAsync(IReadOnlyList<Guid> ids, PropertyStatus newStatus, CancellationToken ct)
    {
        var items = await _db.Properties.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var updated = 0;
        var now = DateTime.UtcNow;
        foreach (var p in items)
        {
            if (p.Status == newStatus) continue;
            p.Status = newStatus;
            p.UpdatedAt = now;
            switch (newStatus)
            {
                case PropertyStatus.Approved: p.ApprovedAt = now; break;
                case PropertyStatus.Recycled: p.RecycledAt = now; break;
                case PropertyStatus.Inbox:
                    p.ApprovedAt = null;
                    p.RecycledAt = null;
                    break;
            }
            updated++;
        }
        return updated;
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct)
    {
        var inbox = await _db.Properties.CountAsync(p => p.Status == PropertyStatus.Inbox, ct);
        var approved = await _db.Properties.CountAsync(p => p.Status == PropertyStatus.Approved, ct);
        var recycled = await _db.Properties.CountAsync(p => p.Status == PropertyStatus.Recycled, ct);

        decimal avg = 0m, median = 0m;
        var approvedPrices = await _db.Properties
            .Where(p => p.Status == PropertyStatus.Approved)
            .Select(p => p.PriceMonthly)
            .ToListAsync(ct);

        if (approvedPrices.Count > 0)
        {
            avg = Math.Round(approvedPrices.Average(), 2);
            approvedPrices.Sort();
            var n = approvedPrices.Count;
            median = n % 2 == 1
                ? approvedPrices[n / 2]
                : Math.Round((approvedPrices[(n / 2) - 1] + approvedPrices[n / 2]) / 2m, 2);
        }

        return new StatsDto(inbox, approved, recycled, avg, median);
    }
}

/// <summary>
/// Marker methods mapped to SQLite scalar functions at query translation time.
/// Calls to these methods are translated by EF Core via DbFunction mapping.
/// </summary>
public static class SqliteFunctions
{
    [DbFunction("berrank", IsBuiltIn = false)]
    public static int BerRank(string? ber) => Domain.ValueObjects.BerRank.Rank(ber);
}
