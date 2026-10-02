using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BrandServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.MediaServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingConfirmationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.NativeAppStatusServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalletPassServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.GuestReminderServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WaitlistReadyNotifierTests")]
[ExternalAccessAllowed]
internal class BrandSettingsRepository(AppDbContext db) : IBrandSettingsRepository
{
    private readonly AppDbContext _db = db;

    public async Task<BrandSettings?> GetAsync()
    {
        return await _db.Set<BrandSettings>().FirstOrDefaultAsync();
    }

    public async Task<BrandSettings> AddAsync(BrandSettings brand)
    {
        _db.Set<BrandSettings>().Add(brand);
        await _db.SaveChangesAsync();
        return brand;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
