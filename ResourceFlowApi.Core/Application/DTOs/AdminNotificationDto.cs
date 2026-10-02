namespace ResourceFlowApi.Core.Application.DTOs;

public record AdminNotificationDto(
    int Id,
    int VenueId,
    string VenueName,
    int? BookingId,
    string BookingRef,
    string Type,
    string CustomerName,
    DateTime BookingDate,
    int PartySize,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? PushSentAt,
    string? PushError
);

public record PushSubscribeRequest(
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent = null
);

// Internal push payload serialised as the Web Push body
public record PushPayload(
    string Title,
    string Body,
    string Type,
    int? BookingId,
    string? BookingRef,
    int VenueId
);
