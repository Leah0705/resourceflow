using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Settings;
using ResourceFlowApi.Core.Domain;
using WebPush;

namespace ResourceFlowApi.Core.Application.Services;

/// <inheritdoc cref="IBookingNotificationService" />
public sealed class BookingNotificationService(
    IAdminNotificationRepository notificationRepository,
    IAdminPushSubscriptionRepository pushSubscriptionRepository,
    IResourceRepository resourceRepository,
    IBookingRepository bookingRepository,
    IOptions<VapidSettings> vapidOptions,
    IWebPushClient webPushClient,
    ILogger<BookingNotificationService> logger) : IBookingNotificationService
{
    private readonly IAdminNotificationRepository _notificationRepository = notificationRepository;
    private readonly IAdminPushSubscriptionRepository _pushSubscriptionRepository = pushSubscriptionRepository;
    private readonly IResourceRepository _resourceRepository = resourceRepository;
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly VapidSettings _vapid = vapidOptions.Value;
    private readonly IWebPushClient _webPushClient = webPushClient;
    private readonly ILogger<BookingNotificationService> _log = logger;

    // Fraction of resources booked on a day that triggers the VenueNearlyFull notification
    private const double _capacityThreshold = 0.8;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Notify ───────────────────────────────────────────────────────────────

    public async Task NotifyBookingCreatedAsync(Booking booking, string venueName)
    {
        _log.LogInformation("[Notif] BookingCreated: ref={Ref} venue={Venue} partySize={PartySize}",
            booking.BookingRef, venueName, booking.PartySize);

        var notification = new AdminNotification
        {
            VenueId = booking.VenueId,
            BookingId = booking.Id,
            BookingRef = booking.BookingRef,
            Type = NotificationType.BookingCreated,
            CustomerName = booking.CustomerName ?? "Guest",
            BookingDate = booking.Date,
            PartySize = booking.PartySize,
            VenueName = venueName,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };
        await _notificationRepository.AddAsync(notification);
        _log.LogInformation("[Notif] BookingCreated saved id={Id}", notification.Id);

        string localTime = FormatUtcAsLocalTime(booking.Date);
        await SendPushAsync(
            booking.VenueId,
            notification.Id,
            new PushPayload(
                Title: $"New booking - {venueName}",
                Body: $"{booking.CustomerName ?? "Guest"} · {booking.PartySize} guest{(booking.PartySize == 1 ? "" : "s")} · {localTime}",
                Type: NotificationType.BookingCreated,
                BookingId: booking.Id,
                BookingRef: booking.BookingRef,
                VenueId: booking.VenueId
            ));
    }

    public async Task NotifyBookingCancelledAsync(Booking booking, string venueName)
    {
        _log.LogInformation("[Notif] BookingCancelled: ref={Ref} venue={Venue}",
            booking.BookingRef, venueName);

        var notification = new AdminNotification
        {
            VenueId = booking.VenueId,
            BookingId = booking.Id,
            BookingRef = booking.BookingRef,
            Type = NotificationType.BookingCancelled,
            CustomerName = booking.CustomerName ?? "Guest",
            BookingDate = booking.Date,
            PartySize = booking.PartySize,
            VenueName = venueName,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };
        await _notificationRepository.AddAsync(notification);
        _log.LogInformation("[Notif] BookingCancelled saved id={Id}", notification.Id);

        string localTime = FormatUtcAsLocalTime(booking.Date);
        await SendPushAsync(
            booking.VenueId,
            notification.Id,
            new PushPayload(
                Title: $"Booking cancelled - {venueName}",
                Body: $"{booking.CustomerName ?? "Guest"} · {booking.PartySize} guest{(booking.PartySize == 1 ? "" : "s")} · {localTime}",
                Type: NotificationType.BookingCancelled,
                BookingId: booking.Id,
                BookingRef: booking.BookingRef,
                VenueId: booking.VenueId
            ));
    }

    public async Task CheckAndNotifyCapacityAsync(int venueId, string venueName, DateTime bookingDate)
    {
        // Count distinct resources booked on the same UTC calendar day
        DateTime dayStart = bookingDate.Date;
        DateTime dayEnd = dayStart.AddDays(1);

        int totalResources = await _resourceRepository.CountByVenueAsync(venueId);

        if (totalResources == 0)
        {
            _log.LogDebug("[Notif] Capacity check skipped: no resources for venue {VenueId}", venueId);
            return;
        }

        int bookedResources = await _bookingRepository.CountDistinctBookedResourcesAsync(venueId, dayStart, dayEnd);

        double ratio = (double)bookedResources / totalResources;
        double previousRatio = (double)(bookedResources - 1) / totalResources;

        _log.LogDebug("[Notif] Capacity check: venue={Venue} booked={Booked}/{Total} ratio={Ratio:P0} threshold={Threshold:P0}",
            venueName, bookedResources, totalResources, ratio, _capacityThreshold);

        // Only fire when this booking pushes us across the threshold for the first time
        if (ratio < _capacityThreshold || previousRatio >= _capacityThreshold) return;

        // Deduplicate: don't fire if we already have a VenueNearlyFull notification for today
        bool alreadyFired = await _notificationRepository.ExistsNearlyFullForDayAsync(venueId, dayStart, dayEnd);

        if (alreadyFired)
        {
            _log.LogDebug("[Notif] NearlyFull already fired today for venue {VenueId}", venueId);
            return;
        }

        var notification = new AdminNotification
        {
            VenueId = venueId,
            BookingId = null,
            BookingRef = string.Empty,
            Type = NotificationType.VenueNearlyFull,
            CustomerName = string.Empty,
            BookingDate = bookingDate,
            PartySize = 0,
            VenueName = venueName,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };
        await _notificationRepository.AddAsync(notification);

        await SendPushAsync(
            venueId,
            notification.Id,
            new PushPayload(
                Title: $"Nearly full - {venueName}",
                Body: $"{bookedResources} of {totalResources} resources booked today ({(int)(ratio * 100)}%)",
                Type: NotificationType.VenueNearlyFull,
                BookingId: null,
                BookingRef: null,
                VenueId: venueId
            ));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SendPushAsync(int venueId, int notificationId, PushPayload payload)
    {
        if (!_vapid.IsConfigured)
        {
            _log.LogDebug("[Push] Skipped — VAPID not configured");
            return;
        }

        // Every subscription, not just this venue's: an admin sees all locations, so a
        // browser that subscribed while viewing one of them is owed the others' bookings too.
        List<AdminPushSubscription> subscriptions = await _pushSubscriptionRepository.GetAllAsync();

        _log.LogInformation("[Push] Sending notificationId={NotifId} (venue {VenueId}) to {Count} subscription(s)",
            notificationId, venueId, subscriptions.Count);

        if (subscriptions.Count == 0) return;

        string json = JsonSerializer.Serialize(payload, _jsonOptions);

        var vapidDetails = new VapidDetails(_vapid.Subject, _vapid.PublicKey, _vapid.PrivateKey);
        List<AdminPushSubscription> stale = [];
        DateTime? sentAt = null;
        string? lastError = null;

        foreach (AdminPushSubscription sub in subscriptions)
        {
            var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
            try
            {
                await _webPushClient.SendNotificationAsync(pushSub, json, vapidDetails);
                sentAt = DateTime.UtcNow;
                _log.LogInformation("[Push] Delivered sub={SubId} notifId={NotifId}", sub.Id, notificationId);
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.Gone ||
                ex.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogWarning("[Push] Stale subscription sub={SubId} — removing", sub.Id);
                stale.Add(sub);
            }
            catch (WebPushException ex)
            {
                lastError = $"HTTP {(int)ex.StatusCode}: {ex.Message}";
                _log.LogError("[Push] Failed sub={SubId}: {Error}", sub.Id, lastError);
            }
            catch (Exception ex)
            {
                // One unusable subscription must not cost every later subscriber their push.
                // WebPush throws outside WebPushException — InvalidEncryptionDetailsException
                // for key material the browser's endpoint will not accept, for one — and an
                // escape here aborts the loop and leaves PushSentAt/PushError unwritten.
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                _log.LogError(ex, "[Push] Failed sub={SubId}: {Error}", sub.Id, lastError);
            }
        }

        // Mirror the original single-SaveChangesAsync: stage stale removals + the notification
        // push-outcome update on the shared DbContext, then flush once.
        if (stale.Count > 0)
        {
            _pushSubscriptionRepository.RemoveRange(stale);
        }

        AdminNotification? record = await _notificationRepository.FindByIdAsync(notificationId);
        if (record is not null)
        {
            record.PushSentAt = sentAt;
            record.PushError = lastError;
        }

        await _notificationRepository.SaveChangesAsync();
    }

    private static string FormatUtcAsLocalTime(DateTime utc) =>
        utc.ToString("ddd d MMM 'at' h:mm tt", CultureInfo.InvariantCulture);
}
