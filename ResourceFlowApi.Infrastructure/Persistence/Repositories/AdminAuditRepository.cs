using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AuditQueryServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AuditRetentionServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.AdminAuditRepositoryTests")]
[ExternalAccessAllowed]
internal class AdminAuditRepository(AppDbContext db) : IAdminAuditRepository
{
    private readonly AppDbContext _db = db;

    public async Task AddAsync(AdminAuditEntry entry)
    {
        _db.AdminAuditEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Newest first, with the id breaking ties so entries written inside the same clock tick don't
    /// shuffle across a page boundary.
    /// <seealso>AdminAuditRepositoryTests.QueryPagedAsync_MatchesActionByPrefix</seealso>
    /// <seealso>AdminAuditRepositoryTests.QueryPagedAsync_PagesDeterministically_WhenTimestampsCollide</seealso>
    /// <seealso>AdminAuditRepositoryTests.QueryPagedAsync_FiltersByDateRangeInclusively</seealso>
    /// </summary>
    public async Task<(List<AdminAuditEntry> Items, int TotalCount)> QueryPagedAsync(
        AuditQuery query, int page, int pageSize)
    {
        IQueryable<AdminAuditEntry> q = _db.AdminAuditEntries.AsNoTracking();

        if (query.ActorUserId.HasValue)
            q = q.Where(e => e.ActorUserId == query.ActorUserId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            string prefix = query.Action.Trim();
            q = q.Where(e => e.Action.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
            q = q.Where(e => e.TargetType == query.TargetType);

        if (query.VenueId.HasValue)
            q = q.Where(e => e.VenueId == query.VenueId.Value);

        if (query.From.HasValue)
            q = q.Where(e => e.OccurredAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(e => e.OccurredAt <= query.To.Value);

        q = q.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id);

        int total = await q.CountAsync();
        List<AdminAuditEntry> items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
        => await _db.AdminAuditEntries.Where(e => e.OccurredAt < cutoffUtc).ExecuteDeleteAsync();
}
