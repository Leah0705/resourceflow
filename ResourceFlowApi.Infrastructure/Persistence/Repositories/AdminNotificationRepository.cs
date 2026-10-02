using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.NotificationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingNotificationServiceTests")]
[ExternalAccessAllowed]
internal class AdminNotificationRepository(AppDbContext db) : IAdminNotificationRepository
{
    private readonly AppDbContext _db = db;

    public async Task<AdminNotification?> FindByIdAsync(int id)
    {
        return await _db.AdminNotifications.FindAsync(id);
    }

    public async Task<AdminNotification> AddAsync(AdminNotification notification)
    {
        _db.AdminNotifications.Add(notification);
        await _db.SaveChangesAsync();
        return notification;
    }

    public async Task<(List<AdminNotification> Items, int TotalCount)> QueryPagedAsync(int? venueId, string? type, bool? unreadOnly, int page, int pageSize)
    {
        IQueryable<AdminNotification> q = _db.AdminNotifications.AsQueryable();

        if (venueId.HasValue)
        {
            q = q.Where(n => n.VenueId == venueId.Value);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            q = q.Where(n => n.Type == type);
        }

        if (unreadOnly == true)
        {
            q = q.Where(n => !n.IsRead);
        }

        q = q.OrderByDescending(n => n.CreatedAt);

        int total = await q.CountAsync();
        List<AdminNotification> items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountUnreadAsync(int? venueId)
    {
        return await _db.AdminNotifications.CountAsync(n =>
            (!venueId.HasValue || n.VenueId == venueId.Value) && !n.IsRead);
    }

    public async Task MarkReadAsync(int notificationId)
    {
        AdminNotification? n = await _db.AdminNotifications.FindAsync(notificationId);
        if (n is { IsRead: false })
        {
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> MarkAllReadAsync(int venueId)
    {
        return await _db.AdminNotifications
            .Where(n => n.VenueId == venueId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }

    public async Task DeleteByIdAsync(int notificationId)
    {
        AdminNotification? n = await _db.AdminNotifications.FindAsync(notificationId);
        if (n is not null)
        {
            _db.AdminNotifications.Remove(n);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteByIdsAsync(List<int> notificationIds)
    {
        List<AdminNotification> notifications = await _db.AdminNotifications
            .Where(n => notificationIds.Contains(n.Id))
            .ToListAsync();

        if (notifications.Count > 0)
        {
            _db.AdminNotifications.RemoveRange(notifications);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> DeleteAllAsync(int? venueId, string? type, bool? unreadOnly)
    {
        IQueryable<AdminNotification> q = _db.AdminNotifications.AsQueryable();

        if (venueId.HasValue)
        {
            q = q.Where(n => n.VenueId == venueId.Value);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            q = q.Where(n => n.Type == type);
        }

        if (unreadOnly == true)
        {
            q = q.Where(n => !n.IsRead);
        }

        return await q.ExecuteDeleteAsync();
    }

    public async Task<bool> ExistsNearlyFullForDayAsync(int venueId, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        return await _db.AdminNotifications.AnyAsync(n =>
            n.VenueId == venueId &&
            n.Type == NotificationType.VenueNearlyFull &&
            n.BookingDate >= dayStartUtc &&
            n.BookingDate < dayEndUtc);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
