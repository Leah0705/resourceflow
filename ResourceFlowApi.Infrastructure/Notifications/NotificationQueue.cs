using System.Threading.Channels;
using CustomAccessibility.Attributes;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Notifications;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Infrastructure.Notifications.*")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationQueueTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationWorkerTests")]
[ExternalAccessAllowed]
internal sealed class NotificationQueue : INotificationQueue
{
    internal readonly Channel<NotificationWorkItem> Channel =
        System.Threading.Channels.Channel.CreateBounded<NotificationWorkItem>(
            new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

    public void EnqueueBookingCreated(Booking booking, string venueName) =>
        Channel.Writer.TryWrite(new BookingCreatedWork(booking, venueName));

    public void EnqueueBookingCancelled(Booking booking, string venueName) =>
        Channel.Writer.TryWrite(new BookingCancelledWork(booking, venueName));

    public void EnqueueCapacityCheck(int venueId, string venueName, DateTime bookingDate) =>
        Channel.Writer.TryWrite(new CapacityCheckWork(venueId, venueName, bookingDate));

    // The CustomAccessibility analyzer's [OnlyAccessibleBy]/[ExternalAccessAllowed]
    // pair on the class only covers construction and the (public,
    // interface-implementing) Enqueue* methods — internal members need the same
    // pair repeated on themselves individually to be callable from the
    // whitelisted test classes, so these carry their own.
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationQueueTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationWorkerTests")]
    [ExternalAccessAllowed]
    internal bool TryReadForTests(out NotificationWorkItem? item) => Channel.Reader.TryRead(out item);

    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationQueueTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationWorkerTests")]
    [ExternalAccessAllowed]
    internal bool TryWriteForTests(NotificationWorkItem item) => Channel.Writer.TryWrite(item);

    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationQueueTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationWorkerTests")]
    [ExternalAccessAllowed]
    internal void CompleteForTests() => Channel.Writer.Complete();
}
