using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Application.Abstractions;

public interface IPropertyRepository
{
    Task<Property?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Property?> GetByDaftIdAsync(string daftId, CancellationToken ct);
    Task AddAsync(Property property, CancellationToken ct);
    Task<PagedResult<Property>> QueryAsync(PropertyQuery query, CancellationToken ct);
    Task<IReadOnlyList<Property>> GetPendingGeocodeAsync(int batchSize, CancellationToken ct);
    Task<int> UpdateStatusAsync(
        IReadOnlyList<Guid> ids,
        PropertyStatus newStatus,
        CancellationToken ct
    );
    Task<StatsDto> GetStatsAsync(CancellationToken ct);
}

public interface IRawEmailRepository
{
    Task<bool> ExistsByMessageIdAsync(string messageId, CancellationToken ct);
    Task AddAsync(RawEmail email, CancellationToken ct);
    Task<RawEmail?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<RawEmail>> GetFailedForRetryAsync(
        TimeSpan minimumAge,
        int batchSize,
        CancellationToken ct
    );
    Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct);
}

public interface IFilterPresetRepository
{
    Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken ct);
    Task<FilterPreset?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(FilterPreset preset, CancellationToken ct);
    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
    Task<bool> AnyAsync(CancellationToken ct);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
