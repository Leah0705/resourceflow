using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Application.Settings;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public NotificationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private NotificationService CreateService(VapidSettings? vapid = null)
    {
        vapid ??= new VapidSettings();
        return new NotificationService(
            new AdminNotificationRepository(_db),
            new AdminPushSubscriptionRepository(_db),
            Options.Create(vapid),
            NullLogger<NotificationService>.Instance);
    }

    private async Task<Venue> SeedVenueAsync(int id = 1)
    {
        var r = new Venue { Id = id, Name = $"Venue {id}", Timezone = "UTC" };
        _db.Venues.Add(r);
        await _db.SaveChangesAsync();
        return r;
    }

    private static Booking MakeBooking(int venueId = 1) => new()
    {
        BookingRef = "ABC123",
        VenueId = venueId,
        CustomerName = "Alice",
        PartySize = 2,
        Date = DateTime.UtcNow.AddDays(1),
    };

    // ── GetVapidPublicKey ─────────────────────────────────────────────────────

    [Fact]
    public void GetVapidPublicKey_ReturnsNull_WhenNotConfigured()
    {
        Assert.Null(CreateService().GetVapidPublicKey());
    }

    [Fact]
    public void GetVapidPublicKey_ReturnsPublicKey_WhenConfigured()
    {
        var svc = CreateService(new VapidSettings
        {
            PublicKey = "my-public-key",
            PrivateKey = "my-private-key",
            Subject = "mailto:a@b.com"
        });
        Assert.Equal("my-public-key", svc.GetVapidPublicKey());
    }

    // Booking-event and capacity-threshold notification tests moved to
    // BookingNotificationServiceTests (those methods now live on IBookingNotificationService,
    // consumed by the background NotificationWorker — no longer on the controller-facing
    // INotificationService surface tested here).

    // ── GetNotificationsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetNotificationsAsync_ReturnsAll_WhenNoFilter()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCreated, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, Type = NotificationType.BookingCancelled, BookingRef = "B", IsRead = true, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        (List<AdminNotificationDto> items, int total) = await CreateService().GetNotificationsAsync(null, null, null, 1, 20);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetNotificationsAsync_FiltersUnreadOnly()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCreated, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCancelled, BookingRef = "B", IsRead = true, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        (List<AdminNotificationDto> items, int total) = await CreateService().GetNotificationsAsync(null, null, true, 1, 20);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.False(i.IsRead));
    }

    [Fact]
    public async Task GetNotificationsAsync_FiltersVenueId()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCreated, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, Type = NotificationType.BookingCreated, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        (List<AdminNotificationDto> items, int total) = await CreateService().GetNotificationsAsync(1, null, null, 1, 20);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.Equal(1, i.VenueId));
    }

    [Fact]
    public async Task GetNotificationsAsync_FiltersType()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCreated, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, Type = NotificationType.BookingCancelled, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        (List<AdminNotificationDto> items, int total) = await CreateService().GetNotificationsAsync(null, NotificationType.BookingCreated, null, 1, 20);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.Equal(NotificationType.BookingCreated, i.Type));
    }

    [Fact]
    public async Task GetNotificationsAsync_PaginatesResults()
    {
        await SeedVenueAsync();
        for (int i = 0; i < 5; i++)
            _db.AdminNotifications.Add(new AdminNotification
            {
                VenueId = 1,
                Type = NotificationType.BookingCreated,
                BookingRef = $"R{i}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddSeconds(i)
            });
        await _db.SaveChangesAsync();

        (List<AdminNotificationDto> page1, int total) = await CreateService().GetNotificationsAsync(null, null, null, 1, 3);

        Assert.Equal(5, total);
        Assert.Equal(3, page1.Count);
    }

    // ── GetUnreadCountAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCountAsync_CountsAllUnread_WhenNoVenueFilter()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, BookingRef = "C", IsRead = true, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        int count = await CreateService().GetUnreadCountAsync(null);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetUnreadCountAsync_FiltersVenue()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        int count = await CreateService().GetUnreadCountAsync(1);
        Assert.Equal(1, count);
    }

    // ── MarkReadAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkReadAsync_SetsIsReadTrue()
    {
        await SeedVenueAsync();
        var n = new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow };
        _db.AdminNotifications.Add(n);
        await _db.SaveChangesAsync();

        await CreateService().MarkReadAsync(n.Id);

        await _db.Entry(n).ReloadAsync();
        Assert.True(n.IsRead);
    }

    [Fact]
    public async Task MarkReadAsync_NoOp_WhenAlreadyRead()
    {
        await SeedVenueAsync();
        var n = new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = true, CreatedAt = DateTime.UtcNow };
        _db.AdminNotifications.Add(n);
        await _db.SaveChangesAsync();

        await CreateService().MarkReadAsync(n.Id);

        await _db.Entry(n).ReloadAsync();
        Assert.True(n.IsRead);
    }

    [Fact]
    public async Task MarkReadAsync_NoOp_WhenNotFound()
    {
        // Should not throw
        await CreateService().MarkReadAsync(9999);
    }

    // ── MarkAllReadAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllReadAsync_MarksAllUnreadForVenue()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, BookingRef = "C", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        await CreateService().MarkAllReadAsync(1);

        Assert.Equal(0, await _db.AdminNotifications.CountAsync(n => n.VenueId == 1 && !n.IsRead));
        Assert.Equal(1, await _db.AdminNotifications.CountAsync(n => n.VenueId == 2 && !n.IsRead));
    }

    // ── SubscribeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_CreatesNewSubscription()
    {
        await SeedVenueAsync();
        await CreateService().SubscribeAsync(new PushSubscribeRequest("https://ep", "p256", "auth"));

        Assert.Equal(1, await _db.AdminPushSubscriptions.CountAsync());
        AdminPushSubscription sub = await _db.AdminPushSubscriptions.FirstAsync();
        Assert.Equal("https://ep", sub.Endpoint);
    }

    [Theory]
    [InlineData("http://ep")]
    [InlineData("https://127.0.0.1/ep")]
    [InlineData("https://backend.internal:8080/ep")]
    [InlineData("not a url")]
    public async Task SubscribeAsync_RejectsAnEndpointThatIsNotAPublicHttpsUrl(string endpoint)
    {
        await SeedVenueAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().SubscribeAsync(new PushSubscribeRequest(endpoint, "p256", "auth")));

        Assert.Equal(ErrorCodes.NotificationPushEndpointInvalid, ex.Code);
        Assert.Equal(0, await _db.AdminPushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task SubscribeAsync_UpdatesExistingSubscription()
    {
        await SeedVenueAsync();
        _db.AdminPushSubscriptions.Add(new AdminPushSubscription
        {
            Endpoint = "https://ep",
            P256dh = "old-p256",
            Auth = "old-auth",
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await CreateService().SubscribeAsync(new PushSubscribeRequest("https://ep", "new-p256", "new-auth"));

        Assert.Equal(1, await _db.AdminPushSubscriptions.CountAsync());
        AdminPushSubscription sub = await _db.AdminPushSubscriptions.FirstAsync();
        Assert.Equal("new-p256", sub.P256dh);
        Assert.Equal("new-auth", sub.Auth);
    }

    // ── UnsubscribeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UnsubscribeAsync_RemovesTheEndpoint_AndLeavesOtherBrowsersAlone()
    {
        _db.AdminPushSubscriptions.AddRange(
            new AdminPushSubscription { Endpoint = "https://ep", P256dh = "p", Auth = "a", CreatedAt = DateTime.UtcNow },
            new AdminPushSubscription { Endpoint = "https://other-ep", P256dh = "p", Auth = "a", CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        await CreateService().UnsubscribeAsync("https://ep");

        AdminPushSubscription remaining = await _db.AdminPushSubscriptions.SingleAsync();
        Assert.Equal("https://other-ep", remaining.Endpoint);
    }

    [Fact]
    public async Task SubscribeAsync_RegistersOnce_WhenTheSameBrowserSubscribesRepeatedly()
    {
        // The browser holds one PushSubscription regardless of which location the admin is
        // looking at, so re-subscribing must refresh the row rather than accumulate one per
        // visit — which is what the old per-venue key did.
        NotificationService svc = CreateService();
        await svc.SubscribeAsync(new PushSubscribeRequest("https://ep", "p256", "auth"));
        await svc.SubscribeAsync(new PushSubscribeRequest("https://ep", "p256", "auth"));
        await svc.SubscribeAsync(new PushSubscribeRequest("https://ep", "p256", "auth"));

        Assert.Equal(1, await _db.AdminPushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task UnsubscribeAsync_NoOp_WhenNotFound()
    {
        // Should not throw
        await CreateService().UnsubscribeAsync("https://not-found");
    }

    // ── DeleteByIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteByIdAsync_RemovesExistingNotification()
    {
        await SeedVenueAsync();
        var n = new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow };
        _db.AdminNotifications.Add(n);
        await _db.SaveChangesAsync();

        await CreateService().DeleteByIdAsync(n.Id);

        Assert.Equal(0, await _db.AdminNotifications.CountAsync());
    }

    [Fact]
    public async Task DeleteByIdAsync_NoOp_WhenNotFound()
    {
        await CreateService().DeleteByIdAsync(9999);
    }

    // ── DeleteByIdsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteByIdsAsync_RemovesSpecifiedIds()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, BookingRef = "C", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        List<int> ids = await _db.AdminNotifications
            .Where(n => n.BookingRef == "A" || n.BookingRef == "B")
            .Select(n => n.Id)
            .ToListAsync();

        await CreateService().DeleteByIdsAsync(ids);

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
        Assert.Equal("C", (await _db.AdminNotifications.FirstAsync()).BookingRef);
    }

    [Fact]
    public async Task DeleteByIdsAsync_NoOp_WhenEmptyList()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.Add(new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await CreateService().DeleteByIdsAsync(new List<int>());

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
    }

    // ── DeleteAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_DeletesAll_WhenNoFilter()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow, Type = NotificationType.BookingCreated },
            new AdminNotification { VenueId = 2, BookingRef = "B", IsRead = true, CreatedAt = DateTime.UtcNow, Type = NotificationType.BookingCancelled }
        );
        await _db.SaveChangesAsync();

        await CreateService().DeleteAllAsync(null, null, null);

        Assert.Equal(0, await _db.AdminNotifications.CountAsync());
    }

    [Fact]
    public async Task DeleteAllAsync_FiltersVenueId()
    {
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 2, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        await CreateService().DeleteAllAsync(1, null, null);

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
        Assert.Equal(2, (await _db.AdminNotifications.FirstAsync()).VenueId);
    }

    [Fact]
    public async Task DeleteAllAsync_FiltersType()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow, Type = NotificationType.BookingCreated },
            new AdminNotification { VenueId = 1, BookingRef = "B", IsRead = false, CreatedAt = DateTime.UtcNow, Type = NotificationType.BookingCancelled }
        );
        await _db.SaveChangesAsync();

        await CreateService().DeleteAllAsync(null, NotificationType.BookingCreated, null);

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
        Assert.Equal(NotificationType.BookingCancelled, (await _db.AdminNotifications.FirstAsync()).Type);
    }

    [Fact]
    public async Task DeleteAllAsync_FiltersUnreadOnly()
    {
        await SeedVenueAsync();
        _db.AdminNotifications.AddRange(
            new AdminNotification { VenueId = 1, BookingRef = "A", IsRead = false, CreatedAt = DateTime.UtcNow },
            new AdminNotification { VenueId = 1, BookingRef = "B", IsRead = true, CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        await CreateService().DeleteAllAsync(null, null, true);

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
        Assert.True((await _db.AdminNotifications.FirstAsync()).IsRead);
    }
}
