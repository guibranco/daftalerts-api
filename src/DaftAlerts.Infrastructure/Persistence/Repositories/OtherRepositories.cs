using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DaftAlerts.Infrastructure.Persistence.Repositories;

public sealed class RawEmailRepository : IRawEmailRepository
{
    private readonly AppDbContext _db;
    public RawEmailRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsByMessageIdAsync(string messageId, CancellationToken ct) =>
        _db.RawEmails.AsNoTracking().AnyAsync(r => r.MessageId == messageId, ct);

    public async Task AddAsync(RawEmail email, CancellationToken ct) =>
        await _db.RawEmails.AddAsync(email, ct);

    public Task<RawEmail?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.RawEmails.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<RawEmail>> GetFailedForRetryAsync(TimeSpan minimumAge, int batchSize, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - minimumAge;
        return await _db.RawEmails
            .Where(r => r.ParseStatus == ParseStatus.Failed &&
                        (r.LastAttemptAt == null || r.LastAttemptAt < cutoff))
            .OrderBy(r => r.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct)
    {
        return await _db.RawEmails.Where(r => r.ReceivedAt < threshold).ExecuteDeleteAsync(ct);
    }
}

public sealed class FilterPresetRepository : IFilterPresetRepository
{
    private readonly AppDbContext _db;
    public FilterPresetRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken ct) =>
        await _db.FilterPresets.OrderByDescending(f => f.IsDefault).ThenBy(f => f.Name).ToListAsync(ct);

    public Task<FilterPreset?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.FilterPresets.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task AddAsync(FilterPreset preset, CancellationToken ct) =>
        await _db.FilterPresets.AddAsync(preset, ct);

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct)
    {
        var existing = await _db.FilterPresets.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (existing is null) return false;
        _db.FilterPresets.Remove(existing);
        return true;
    }

    public Task<bool> AnyAsync(CancellationToken ct) => _db.FilterPresets.AnyAsync(ct);
}

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public EfUnitOfWork(AppDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
