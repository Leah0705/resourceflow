using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AdminServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.VenueManagementServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalkInTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerRestoreTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerSectionsReorderTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerUpdateTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerEmailTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerLookupTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Integration.RepositoryTests")]
[ExternalAccessAllowed]
internal class SectionRepository(AppDbContext db) : ISectionRepository
{
    private readonly AppDbContext _db = db;

    public async Task<Section?> GetByIdAsync(int id)
    {
        return await _db.Sections.FindAsync(id);
    }

    // ── Bundle 2 additions ─────────────────────────────────────────────────────

    public async Task<Section?> FindByIdAsync(int id)
    {
        return await _db.Sections.FindAsync(id);
    }

    public async Task<List<Section>> GetByVenueAsync(int venueId)
    {
        return await _db.Sections
            .Where(s => s.VenueId == venueId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<List<Section>> GetByVenueAsync(int venueId, bool includeResources)
    {
        IQueryable<Section> q = _db.Sections
            .Where(s => s.VenueId == venueId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id);

        if (includeResources)
        {
            q = q.Include(s => s.Resources);
        }

        return await q.ToListAsync();
    }

    public async Task<int> CountByVenueAsync(int venueId)
    {
        return await _db.Sections.CountAsync(s => s.VenueId == venueId);
    }

    public async Task<bool?> ReorderAsync(int venueId, IReadOnlyList<int> sectionIds)
    {
        bool venueExists = await _db.Venues.AnyAsync(r => r.Id == venueId);
        if (!venueExists)
        {
            return null;
        }

        if (sectionIds == null)
        {
            return false;
        }

        List<Section> sections = await _db.Sections
            .Where(s => s.VenueId == venueId)
            .ToListAsync();

        if (sectionIds.Count != sections.Count ||
            sectionIds.Distinct().Count() != sectionIds.Count)
        {
            return false;
        }

        Dictionary<int, Section> sectionsById = sections.ToDictionary(s => s.Id);
        if (sectionIds.Any(id => !sectionsById.ContainsKey(id)))
        {
            return false;
        }

        for (int i = 0; i < sectionIds.Count; i++)
        {
            sectionsById[sectionIds[i]].SortOrder = i;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task AddAsync(Section section)
    {
        _db.Sections.Add(section);
        await _db.SaveChangesAsync();
    }

    public void Remove(Section section)
    {
        _db.Sections.Remove(section);
    }

    public async Task<Section?> FindForVenueAsync(int sectionId, int venueId)
    {
        return await _db.Sections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.VenueId == venueId);
    }

    public async Task<Section?> GetWithResourcesForVenueAsync(int sectionId, int venueId)
    {
        return await _db.Sections
            .Include(s => s.Resources)
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.VenueId == venueId);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
