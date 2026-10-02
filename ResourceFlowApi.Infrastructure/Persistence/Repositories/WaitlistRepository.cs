using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.WaitlistRepositoryTests")]
[ExternalAccessAllowed]
internal class WaitlistRepository(AppDbContext db) : IWaitlistRepository
{
    private readonly AppDbContext _db = db;

    public async Task<WaitlistEntry?> GetByIdAsync(int id)
    {
        return await _db.WaitlistEntries
            .Include(e => e.Venue)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<WaitlistEntry?> GetByRefAsync(string entryRef)
    {
        return await _db.WaitlistEntries
            .Include(e => e.Venue)
            .FirstOrDefaultAsync(e => e.Ref == entryRef);
    }

    public async Task<List<WaitlistEntry>> GetActiveForVenueAsync(int venueId)
    {
        return await _db.WaitlistEntries
            .Where(e => e.VenueId == venueId
                        && (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified))
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync();
    }

    public async Task<int> CountCreatedSinceAsync(int venueId, DateTime sinceUtc)
    {
        return await _db.WaitlistEntries
            .CountAsync(e => e.VenueId == venueId && e.CreatedAt >= sinceUtc);
    }

    public async Task<WaitlistEntry> AddAsync(WaitlistEntry entry)
    {
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }

    public async Task<int> ExpireActiveCreatedBeforeAsync(DateTime cutoffUtc, DateTime nowUtc)
    {
        return await _db.WaitlistEntries
            .Where(e => e.CreatedAt < cutoffUtc
                        && (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, WaitlistStatus.Expired)
                .SetProperty(e => e.ClosedAt, nowUtc)
                .SetProperty(e => e.PushChannel, (string?)null)
                .SetProperty(e => e.PushEndpoint, (string?)null)
                .SetProperty(e => e.PushP256dh, (string?)null)
                .SetProperty(e => e.PushAuth, (string?)null));
    }

    public async Task<int> DeleteCreatedBeforeAsync(DateTime cutoffUtc)
    {
        return await _db.WaitlistEntries
            .Where(e => e.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync();
    }
}
