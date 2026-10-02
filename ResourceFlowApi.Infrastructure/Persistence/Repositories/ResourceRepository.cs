using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AdminServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.NotificationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingNotificationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.VenueManagementServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalkInTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerRestoreTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerSectionsReorderTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerUpdateTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerEmailTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerLookupTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Integration.RepositoryTests")]
[ExternalAccessAllowed]
internal class ResourceRepository(AppDbContext db) : IResourceRepository
{
    private readonly AppDbContext _db = db;

    public async Task<Resource?> GetByIdAsync(int id)
    {
        return await _db.Resources.FindAsync(id);
    }

    // ── Bundle 2 additions ─────────────────────────────────────────────────────

    public async Task<Resource?> FindByIdAsync(int id)
    {
        return await _db.Resources.FindAsync(id);
    }

    public async Task<Resource?> GetWithSectionVenueAsync(int resourceId, int sectionId)
    {
        return await _db.Resources
            .Include(t => t.Section)
                .ThenInclude(s => s!.Venue)
            .FirstOrDefaultAsync(t => t.Id == resourceId && t.SectionId == sectionId);
    }

    public async Task<Resource?> GetWithSectionForVenueAsync(int resourceId, int venueId)
    {
        return await _db.Resources
            .Include(t => t.Section)
            .FirstOrDefaultAsync(t => t.Id == resourceId && t.Section!.VenueId == venueId);
    }

    public async Task<int> CountByVenueAsync(int venueId)
    {
        return await _db.Resources.CountAsync(t => t.Section!.VenueId == venueId);
    }

    public async Task AddAsync(Resource resource)
    {
        _db.Resources.Add(resource);
        await _db.SaveChangesAsync();
    }

    public void Remove(Resource resource)
    {
        _db.Resources.Remove(resource);
    }

    public async Task<Resource?> GetForVenueAsync(int resourceId, int sectionId, int venueId)
    {
        return await _db.Resources
            .Include(t => t.Section)
            .FirstOrDefaultAsync(t => t.Id == resourceId && t.SectionId == sectionId && t.Section!.VenueId == venueId);
    }

    public async Task<List<Resource>> GetManyForVenueAsync(IReadOnlyList<int> resourceIds, int venueId)
    {
        return await _db.Resources
            .Include(t => t.Section)
            .Where(t => resourceIds.Contains(t.Id) && t.Section!.VenueId == venueId)
            .ToListAsync();
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
