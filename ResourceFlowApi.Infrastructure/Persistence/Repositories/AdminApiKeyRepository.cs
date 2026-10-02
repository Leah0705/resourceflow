using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.ApiKeyServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.ApiKeyAuthenticationHandlerTests")]
[ExternalAccessAllowed]
internal class AdminApiKeyRepository(AppDbContext db) : IAdminApiKeyRepository
{
    private readonly AppDbContext _db = db;

    public async Task<AdminApiKey?> GetByIdAsync(int id)
    {
        return await _db.AdminApiKeys.FirstOrDefaultAsync(k => k.Id == id);
    }

    public async Task<List<AdminApiKey>> GetByUserIdAsync(int userId)
    {
        return await _db.AdminApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ThenByDescending(k => k.Id)
            .ToListAsync();
    }

    public async Task<AdminApiKey> AddAsync(AdminApiKey key)
    {
        _db.AdminApiKeys.Add(key);
        await _db.SaveChangesAsync();
        return key;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
