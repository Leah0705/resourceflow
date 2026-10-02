using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.VenueManagementServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalkInTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.ResourceAutoAssignerTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.HoldsControllerUnitTests")]
[ExternalAccessAllowed]
internal class ResourceGroupRepository(AppDbContext db) : IResourceGroupRepository
{
    private readonly AppDbContext _db = db;

    public async Task<ResourceGroup?> GetByIdWithMembersAsync(int groupId, int venueId)
    {
        return await _db.ResourceGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.Resource)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.VenueId == venueId);
    }

    public async Task<List<ResourceGroup>> GetAllWithMembersByVenueAsync(int venueId)
    {
        return await _db.ResourceGroups
            .Where(g => g.VenueId == venueId)
            .Include(g => g.Members)
                .ThenInclude(m => m.Resource)
            .OrderBy(g => g.Id)
            .ToListAsync();
    }

    public async Task AddAsync(ResourceGroup group)
    {
        _db.ResourceGroups.Add(group);
        await _db.SaveChangesAsync();
    }

    public void Remove(ResourceGroup group)
    {
        _db.ResourceGroups.Remove(group);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
