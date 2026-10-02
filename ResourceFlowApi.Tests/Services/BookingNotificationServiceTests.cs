using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Settings;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;
using WebPush;
using WebPush.Model;

namespace ResourceFlowApi.Tests.Services;

public class BookingNotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Mock<IWebPushClient> _webPushClientMock = new();

    public BookingNotificationServiceTests()
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

    private static VapidSettings ConfiguredVapid() => new()
    {
        Subject = "mailto:ops@resourceflow.example",
        PublicKey = "BPUBLICKEY",
        PrivateKey = "PRIVATEKEY",
    };

    private BookingNotificationService CreateService(VapidSettings? vapid = null)
    {
        vapid ??= new VapidSettings();
        return new BookingNotificationService(
            new AdminNotificationRepository(_db),
            new AdminPushSubscriptionRepository(_db),
            new ResourceRepository(_db),
            new BookingRepository(_db),
            Options.Create(vapid),
            _webPushClientMock.Object,
            NullLogger<BookingNotificationService>.Instance);
    }

    private async Task<AdminPushSubscription> SeedPushSubscriptionAsync(string endpoint = "https://push.example/sub1")
    {
        var sub = new AdminPushSubscription
        {
            Endpoint = endpoint,
            P256dh = "p256dh-key",
            Auth = "auth-secret",
        };
        _db.AdminPushSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    private static WebPushException MakeWebPushException(HttpStatusCode statusCode, PushSubscription? subscription = null)
    {
        var response = new HttpResponseMessage(statusCode);
        return new WebPushException("push failed", subscription ?? new PushSubscription("https://push.example/sub1", "p256dh-key", "auth-secret"), response);
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

    // ── NotifyBookingCreatedAsync ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyBookingCreatedAsync_CreatesNotification()
    {
        await SeedVenueAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await CreateService().NotifyBookingCreatedAsync(booking, "My Venue");

        AdminNotification? n = await _db.AdminNotifications.FirstOrDefaultAsync();
        Assert.NotNull(n);
        Assert.Equal(NotificationType.BookingCreated, n.Type);
        Assert.Equal("My Venue", n.VenueName);
        Assert.Equal("Alice", n.CustomerName);
        Assert.Equal(2, n.PartySize);
        Assert.False(n.IsRead);
    }

    [Fact]
    public async Task NotifyBookingCreatedAsync_UsesGuestFallback_WhenCustomerNameNull()
    {
        await SeedVenueAsync();
        var booking = MakeBooking();
        booking.CustomerName = null;
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await CreateService().NotifyBookingCreatedAsync(booking, "Location");

        AdminNotification? n = await _db.AdminNotifications.FirstOrDefaultAsync();
        Assert.Equal("Guest", n!.CustomerName);
    }

    // ── NotifyBookingCancelledAsync ───────────────────────────────────────────

    [Fact]
    public async Task NotifyBookingCancelledAsync_CreatesNotification()
    {
        await SeedVenueAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await CreateService().NotifyBookingCancelledAsync(booking, "Location");

        AdminNotification? n = await _db.AdminNotifications.FirstOrDefaultAsync();
        Assert.NotNull(n);
        Assert.Equal(NotificationType.BookingCancelled, n.Type);
    }

    [Fact]
    public async Task NotifyBookingCancelledAsync_UsesGuestFallback_WhenCustomerNameNull()
    {
        await SeedVenueAsync();
        var booking = MakeBooking();
        booking.CustomerName = null;
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await CreateService().NotifyBookingCancelledAsync(booking, "Location");

        AdminNotification? n = await _db.AdminNotifications.FirstOrDefaultAsync();
        Assert.Equal("Guest", n!.CustomerName);
    }

    // ── CheckAndNotifyCapacityAsync ───────────────────────────────────────────

    [Fact]
    public async Task CheckAndNotifyCapacityAsync_SkipsWhenNoResources()
    {
        await CreateService().CheckAndNotifyCapacityAsync(1, "Location", DateTime.UtcNow);
        Assert.Empty(await _db.AdminNotifications.ToListAsync());
    }

    [Fact]
    public async Task CheckAndNotifyCapacityAsync_FiresWhenThresholdCrossed()
    {
        await SeedVenueAsync();
        var section = new Section { Name = "Main", VenueId = 1 };
        _db.Sections.Add(section);
        await _db.SaveChangesAsync();
        for (int i = 0; i < 5; i++)
            _db.Resources.Add(new Resource { Name = $"T{i}", Capacity = 4, SectionId = section.Id });
        await _db.SaveChangesAsync();

        // 4/5 = 80% — just crosses the 0.8 threshold
        DateTime date = DateTime.UtcNow.Date.AddHours(12);
        int resourceId = 1;
        foreach (Resource t in await _db.Resources.ToListAsync())
        {
            if (resourceId > 4) break;
            _db.Bookings.Add(new Booking
            {
                BookingRef = $"REF{resourceId}",
                VenueId = 1,
                ResourceId = t.Id,
                Date = date,
                PartySize = 2,
                IsCancelled = false,
            });
            resourceId++;
        }
        await _db.SaveChangesAsync();

        await CreateService().CheckAndNotifyCapacityAsync(1, "Location", date);

        AdminNotification? n = await _db.AdminNotifications.FirstOrDefaultAsync();
        Assert.NotNull(n);
        Assert.Equal(NotificationType.VenueNearlyFull, n.Type);
    }

    [Fact]
    public async Task CheckAndNotifyCapacityAsync_SkipsBelowThreshold()
    {
        await SeedVenueAsync();
        var section = new Section { Name = "Main", VenueId = 1 };
        _db.Sections.Add(section);
        await _db.SaveChangesAsync();
        for (int i = 0; i < 5; i++)
            _db.Resources.Add(new Resource { Name = $"T{i}", Capacity = 4, SectionId = section.Id });
        await _db.SaveChangesAsync();

        // 3/5 = 60% — below 80% threshold
        DateTime date = DateTime.UtcNow.Date.AddHours(12);
        int count = 0;
        foreach (Resource t in await _db.Resources.ToListAsync())
        {
            if (count >= 3) break;
            _db.Bookings.Add(new Booking { BookingRef = $"R{count}", VenueId = 1, ResourceId = t.Id, Date = date, PartySize = 2, IsCancelled = false });
            count++;
        }
        await _db.SaveChangesAsync();

        await CreateService().CheckAndNotifyCapacityAsync(1, "Location", date);

        Assert.Empty(await _db.AdminNotifications.ToListAsync());
    }

    [Fact]
    public async Task CheckAndNotifyCapacityAsync_DeduplicatesNearlyFull()
    {
        await SeedVenueAsync();
        var section = new Section { Name = "Main", VenueId = 1 };
        _db.Sections.Add(section);
        await _db.SaveChangesAsync();
        for (int i = 0; i < 5; i++)
            _db.Resources.Add(new Resource { Name = $"T{i}", Capacity = 4, SectionId = section.Id });
        await _db.SaveChangesAsync();

        DateTime date = DateTime.UtcNow.Date.AddHours(12);
        int count = 0;
        foreach (Resource t in await _db.Resources.ToListAsync())
        {
            if (count >= 4) break;
            _db.Bookings.Add(new Booking { BookingRef = $"R{count}", VenueId = 1, ResourceId = t.Id, Date = date, PartySize = 2, IsCancelled = false });
            count++;
        }
        // Seed an existing NearlyFull notification for today
        _db.AdminNotifications.Add(new AdminNotification
        {
            VenueId = 1,
            Type = NotificationType.VenueNearlyFull,
            BookingDate = date,
            BookingRef = string.Empty,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await CreateService().CheckAndNotifyCapacityAsync(1, "Location", date);

        Assert.Equal(1, await _db.AdminNotifications.CountAsync());
    }

    // ── SendPushAsync (via NotifyBookingCreatedAsync) ─────────────────────────

    [Fact]
    public async Task SendPushAsync_DoesNotCallClient_WhenNoSubscriptions()
    {
        await SeedVenueAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location");

        _webPushClientMock.Verify(
            c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendPushAsync_Delivers_AndRecordsPushSentAt_OnSuccess()
    {
        await SeedVenueAsync();
        await SeedPushSubscriptionAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location");

        AdminNotification notification = await _db.AdminNotifications.SingleAsync();
        Assert.NotNull(notification.PushSentAt);
        Assert.Null(notification.PushError);
        Assert.Equal(1, await _db.AdminPushSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendPushAsync_RemovesStaleSubscription_OnGoneOrNotFound(HttpStatusCode statusCode)
    {
        await SeedVenueAsync();
        await SeedPushSubscriptionAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeWebPushException(statusCode));

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location");

        Assert.Empty(await _db.AdminPushSubscriptions.ToListAsync());
        AdminNotification notification = await _db.AdminNotifications.SingleAsync();
        Assert.Null(notification.PushSentAt);
        Assert.Null(notification.PushError);
    }

    [Fact]
    public async Task SendPushAsync_RecordsPushError_OnOtherFailure_AndKeepsSubscription()
    {
        await SeedVenueAsync();
        await SeedPushSubscriptionAsync();
        var booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeWebPushException(HttpStatusCode.InternalServerError));

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location");

        Assert.Equal(1, await _db.AdminPushSubscriptions.CountAsync());
        AdminNotification notification = await _db.AdminNotifications.SingleAsync();
        Assert.Null(notification.PushSentAt);
        Assert.NotNull(notification.PushError);
        Assert.Contains("500", notification.PushError);
    }

    [Fact]
    public async Task SendPushAsync_DeliversToEverySubscription_NotJustTheBookedVenues()
    {
        // The reported bug: a browser that subscribed while viewing venue 1 got nothing
        // for a booking at venue 2, because the fan-out only read that venue's rows.
        await SeedVenueAsync(1);
        await SeedVenueAsync(2);
        await SeedPushSubscriptionAsync();
        Booking booking = MakeBooking(venueId: 2);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location 2");

        _webPushClientMock.Verify(
            c => c.SendNotificationAsync(It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()),
            Times.Once);
        AdminNotification notification = await _db.AdminNotifications.SingleAsync();
        Assert.NotNull(notification.PushSentAt);
    }

    [Fact]
    public async Task SendPushAsync_KeepsGoing_WhenOneSubscriptionThrowsOutsideWebPushException()
    {
        // WebPush raises InvalidEncryptionDetailsException — not a WebPushException — for key
        // material it cannot encrypt with. Unguarded it escaped the loop, so every later
        // subscriber was skipped and the notification's push outcome was never written.
        await SeedVenueAsync();
        await SeedPushSubscriptionAsync("https://push.example/broken");
        await SeedPushSubscriptionAsync("https://push.example/healthy");
        Booking booking = MakeBooking();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(
                It.Is<PushSubscription>(ps => ps.Endpoint == "https://push.example/broken"),
                It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidEncryptionDetailsException(
                "Unable to encrypt the payload with the encryption key of this subscription.",
                new PushSubscription("https://push.example/broken", "p256dh-key", "auth-secret")));
        _webPushClientMock
            .Setup(c => c.SendNotificationAsync(
                It.Is<PushSubscription>(ps => ps.Endpoint == "https://push.example/healthy"),
                It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateService(ConfiguredVapid()).NotifyBookingCreatedAsync(booking, "Location");

        // The healthy subscriber was still reached, and the outcome reached the database.
        _webPushClientMock.Verify(
            c => c.SendNotificationAsync(
                It.Is<PushSubscription>(ps => ps.Endpoint == "https://push.example/healthy"),
                It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()),
            Times.Once);
        AdminNotification notification = await _db.AdminNotifications.SingleAsync();
        Assert.NotNull(notification.PushSentAt);
        Assert.NotNull(notification.PushError);
        Assert.Contains(nameof(InvalidEncryptionDetailsException), notification.PushError);
        // A bad key is not a dead endpoint, so the row stays for the browser to refresh.
        Assert.Equal(2, await _db.AdminPushSubscriptions.CountAsync());
    }
}
