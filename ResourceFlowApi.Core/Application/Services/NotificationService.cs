using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Settings;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

public class NotificationService(
    IAdminNotificationRepository notificationRepository,
    IAdminPushSubscriptionRepository pushSubscriptionRepository,
    IOptions<VapidSettings> vapidOptions,
    ILogger<NotificationService> logger,
    IAuditScope? audit = null) : INotificationService
{
    private readonly IAdminNotificationRepository _notificationRepository = notificationRepository;
    private readonly IAdminPushSubscriptionRepository _pushSubscriptionRepository = pushSubscriptionRepository;
    private readonly VapidSettings _vapid = vapidOptions.Value;
    private readonly ILogger<NotificationService> _log = logger;
    private readonly IAuditScope _audit = audit ?? NullAuditScope.Instance;

    public string? GetVapidPublicKey() =>
        _vapid.IsConfigured ? _vapid.PublicKey : null;

    // ── Query ─────────────────────────────────────────────────────────────────

    public async Task<(List<AdminNotificationDto> Items, int TotalCount)> GetNotificationsAsync(
        int? venueId, string? type, bool? unreadOnly, int page, int pageSize)
    {
        (List<AdminNotification> items, int total) = await _notificationRepository.QueryPagedAsync(venueId, type, unreadOnly, page, pageSize);
        return (items.Select(ToDto).ToList(), total);
    }

    public async Task<int> GetUnreadCountAsync(int? venueId) =>
        await _notificationRepository.CountUnreadAsync(venueId);

    public async Task MarkReadAsync(int notificationId) =>
        await _notificationRepository.MarkReadAsync(notificationId);

    public async Task MarkAllReadAsync(int venueId) =>
        await _notificationRepository.MarkAllReadAsync(venueId);

    // ── Push subscriptions ────────────────────────────────────────────────────

    /// <summary>
    /// <seealso>NotificationServiceTests.SubscribeAsync_RejectsAnEndpointThatIsNotAPublicHttpsUrl</seealso>
    /// </summary>
    public async Task SubscribeAsync(PushSubscribeRequest request)
    {
        if (!PushEndpointValidator.IsValidWebPushEndpoint(request.Endpoint))
        {
            throw new ValidationException("The push endpoint must be a public https URL.") { Code = ErrorCodes.NotificationPushEndpointInvalid };
        }

        // Deduplicate per endpoint: one row covers every location, so re-subscribing from
        // the same browser refreshes the keys rather than adding a row per venue.
        AdminPushSubscription? existing = await _pushSubscriptionRepository.GetByEndpointAsync(request.Endpoint);

        if (existing is not null)
        {
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.UserAgent = request.UserAgent;
            _log.LogInformation("[Push] Updated subscription id={Id}", existing.Id);
            await _pushSubscriptionRepository.SaveChangesAsync();
        }
        else
        {
            await _pushSubscriptionRepository.AddAsync(new AdminPushSubscription
            {
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                UserAgent = request.UserAgent,
                CreatedAt = DateTime.UtcNow,
            });
            _log.LogInformation("[Push] New subscription registered");
        }

        // No endpoint or key material: a push subscription's keys are credentials.
        // No venueId either — the subscription covers every location.
        _audit.Describe(AuditActions.PushSubscribe,
            summary: "Enabled push notifications for a device");
    }

    public async Task UnsubscribeAsync(string endpoint)
    {
        AdminPushSubscription? sub = await _pushSubscriptionRepository.GetByEndpointAsync(endpoint);
        if (sub is not null)
        {
            _pushSubscriptionRepository.RemoveRange([sub]);
            await _pushSubscriptionRepository.SaveChangesAsync();
            _log.LogInformation("[Push] Unsubscribed subscription id={Id}", sub.Id);
            _audit.Describe(AuditActions.PushUnsubscribe,
                summary: "Disabled push notifications for a device");
        }
        else
        {
            _log.LogWarning("[Push] Unsubscribe called but no matching subscription found");
        }
    }

    public async Task DeleteByIdAsync(int notificationId)
    {
        await _notificationRepository.DeleteByIdAsync(notificationId);
        _audit.Describe(AuditActions.NotificationDelete, AuditTargets.Notification,
            AuditTargets.IdOf(notificationId), summary: "Deleted 1 notification");
    }

    public async Task DeleteByIdsAsync(List<int> notificationIds)
    {
        await _notificationRepository.DeleteByIdsAsync(notificationIds);
        _audit.Describe(AuditActions.NotificationDelete, AuditTargets.Notification,
            summary: $"Deleted {notificationIds.Count} notifications");
    }

    public async Task DeleteAllAsync(int? venueId, string? type, bool? unreadOnly)
    {
        await _notificationRepository.DeleteAllAsync(venueId, type, unreadOnly);
        _audit.Describe(AuditActions.NotificationDelete, AuditTargets.Notification,
            venueId: venueId, summary: "Cleared the matching notifications");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AdminNotificationDto ToDto(AdminNotification n) => new(
        n.Id,
        n.VenueId,
        n.VenueName,
        n.BookingId,
        n.BookingRef,
        n.Type,
        n.CustomerName,
        n.BookingDate,
        n.PartySize,
        n.IsRead,
        n.CreatedAt,
        n.PushSentAt,
        n.PushError
    );
}
