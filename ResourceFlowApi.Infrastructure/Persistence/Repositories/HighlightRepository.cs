using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.HighlightServiceTests")]
[ExternalAccessAllowed]
internal class HighlightRepository(AppDbContext db) : IHighlightRepository
{
    private readonly AppDbContext _db = db;

    public async Task<List<VenueHighlight>> GetAllAsync()
    {
        return await _db.Highlights
            .OrderBy(h => h.SortOrder)
            .ThenBy(h => h.Id)
            .ToListAsync();
    }

    public async Task<VenueHighlight?> FindByIdAsync(int id)
    {
        return await _db.Highlights.FindAsync(id);
    }

    public async Task<VenueHighlight> AddAsync(VenueHighlight highlight)
    {
        _db.Highlights.Add(highlight);
        await _db.SaveChangesAsync();
        return highlight;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }

    public void Remove(VenueHighlight highlight)
    {
        _db.Highlights.Remove(highlight);
    }
}
