using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.NotificationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingNotificationServiceTests")]
[ExternalAccessAllowed]
internal class AdminPushSubscriptionRepository(AppDbContext db) : IAdminPushSubscriptionRepository
{
    private readonly AppDbContext _db = db;

    public async Task<AdminPushSubscription?> GetByEndpointAsync(string endpoint)
    {
        return await _db.AdminPushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint);
    }

    public async Task<List<AdminPushSubscription>> GetAllAsync()
    {
        return await _db.AdminPushSubscriptions.ToListAsync();
    }

    public async Task<AdminPushSubscription> AddAsync(AdminPushSubscription subscription)
    {
        _db.AdminPushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        return subscription;
    }

    public void RemoveRange(IEnumerable<AdminPushSubscription> subscriptions)
    {
        _db.AdminPushSubscriptions.RemoveRange(subscriptions);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
