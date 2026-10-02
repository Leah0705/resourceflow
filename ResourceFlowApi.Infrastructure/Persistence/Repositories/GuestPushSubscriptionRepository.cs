using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.GuestReminderServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalletPassServiceTests")]
[ExternalAccessAllowed]
internal class GuestPushSubscriptionRepository(AppDbContext db) : IGuestPushSubscriptionRepository
{
    public Task<GuestPushSubscription?> GetByBookingAndEndpointAsync(int bookingId, string endpoint) =>
        db.GuestPushSubscriptions.FirstOrDefaultAsync(s => s.BookingId == bookingId && s.Endpoint == endpoint);

    public Task<List<GuestPushSubscription>> GetUpcomingAsync(DateTime nowUtc) =>
        db.GuestPushSubscriptions
            .Include(s => s.Booking).ThenInclude(b => b.Venue)
            .Where(s => !s.Booking.IsCancelled && s.Booking.Date > nowUtc)
            .OrderBy(s => s.Booking.Date)
            .ToListAsync();

    public async Task<int> PruneSpentAsync(DateTime nowUtc)
    {
        List<GuestPushSubscription> spent = await db.GuestPushSubscriptions
            .Where(s => s.Booking.IsCancelled || s.Booking.Date <= nowUtc)
            .ToListAsync();
        if (spent.Count == 0)
        {
            return 0;
        }

        db.GuestPushSubscriptions.RemoveRange(spent);
        await db.SaveChangesAsync();
        return spent.Count;
    }

    public async Task<GuestPushSubscription> AddAsync(GuestPushSubscription subscription)
    {
        db.GuestPushSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    public void RemoveRange(IEnumerable<GuestPushSubscription> subscriptions) =>
        db.GuestPushSubscriptions.RemoveRange(subscriptions);

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
