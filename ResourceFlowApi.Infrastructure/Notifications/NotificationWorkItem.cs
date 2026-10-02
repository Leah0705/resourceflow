using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Notifications;

public abstract record NotificationWorkItem;

public sealed record BookingCreatedWork(Booking Booking, string VenueName) : NotificationWorkItem;
public sealed record BookingCancelledWork(Booking Booking, string VenueName) : NotificationWorkItem;
public sealed record CapacityCheckWork(int VenueId, string VenueName, DateTime BookingDate) : NotificationWorkItem;
