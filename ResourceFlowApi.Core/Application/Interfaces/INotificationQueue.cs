using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface INotificationQueue
{
    void EnqueueBookingCreated(Booking booking, string venueName);
    void EnqueueBookingCancelled(Booking booking, string venueName);
    void EnqueueCapacityCheck(int venueId, string venueName, DateTime bookingDate);
}
