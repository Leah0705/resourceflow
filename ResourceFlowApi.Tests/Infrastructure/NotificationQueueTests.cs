using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Notifications;

namespace ResourceFlowApi.Tests.Infrastructure;

public class NotificationQueueTests
{
    private static Booking CreateBooking() => new()
    {
        VenueId = 1,
        BookingRef = "REF1",
        CustomerName = "Jane",
        CustomerEmail = "jane@test.com",
        PartySize = 2,
        Date = DateTime.UtcNow,
    };

    [Fact]
    public void EnqueueBookingCreated_WritesBookingCreatedWork()
    {
        var queue = new NotificationQueue();
        Booking booking = CreateBooking();

        queue.EnqueueBookingCreated(booking, "Test Location");

        Assert.True(queue.TryReadForTests(out NotificationWorkItem? item));
        var work = Assert.IsType<BookingCreatedWork>(item);
        Assert.Same(booking, work.Booking);
        Assert.Equal("Test Location", work.VenueName);
    }

    [Fact]
    public void EnqueueBookingCancelled_WritesBookingCancelledWork()
    {
        var queue = new NotificationQueue();
        Booking booking = CreateBooking();

        queue.EnqueueBookingCancelled(booking, "Test Location");

        Assert.True(queue.TryReadForTests(out NotificationWorkItem? item));
        var work = Assert.IsType<BookingCancelledWork>(item);
        Assert.Same(booking, work.Booking);
        Assert.Equal("Test Location", work.VenueName);
    }

    [Fact]
    public void EnqueueCapacityCheck_WritesCapacityCheckWork()
    {
        var queue = new NotificationQueue();
        DateTime date = DateTime.UtcNow;

        queue.EnqueueCapacityCheck(42, "Test Location", date);

        Assert.True(queue.TryReadForTests(out NotificationWorkItem? item));
        var work = Assert.IsType<CapacityCheckWork>(item);
        Assert.Equal(42, work.VenueId);
        Assert.Equal("Test Location", work.VenueName);
        Assert.Equal(date, work.BookingDate);
    }

    [Fact]
    public void ImplementsINotificationQueue()
    {
        var queue = new NotificationQueue();
        Assert.IsAssignableFrom<ResourceFlowApi.Core.Application.Interfaces.INotificationQueue>(queue);
    }
}
