using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.EmailSettingsServiceTests")]
[ExternalAccessAllowed]
internal class EmailSettingsRepository(AppDbContext db) : IEmailSettingsRepository
{
    private readonly AppDbContext _db = db;

    public async Task<EmailSettings?> GetAsync()
    {
        return await _db.Set<EmailSettings>().FirstOrDefaultAsync();
    }

    public async Task<EmailSettings> AddAsync(EmailSettings settings)
    {
        _db.Set<EmailSettings>().Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
