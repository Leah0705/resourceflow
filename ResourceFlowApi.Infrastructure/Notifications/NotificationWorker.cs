using CustomAccessibility.Attributes;
using ResourceFlowApi.Core.Application.Interfaces;

namespace ResourceFlowApi.Infrastructure.Notifications;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Infrastructure.NotificationWorkerTests")]
[ExternalAccessAllowed]
internal sealed class NotificationWorker(
    NotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (NotificationWorkItem item in queue.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IBookingNotificationService svc = scope.ServiceProvider.GetRequiredService<IBookingNotificationService>();
            try
            {
                await (item switch
                {
                    BookingCreatedWork w => svc.NotifyBookingCreatedAsync(w.Booking, w.VenueName),
                    BookingCancelledWork w => svc.NotifyBookingCancelledAsync(w.Booking, w.VenueName),
                    CapacityCheckWork w => svc.CheckAndNotifyCapacityAsync(w.VenueId, w.VenueName, w.BookingDate),
                    _ => Task.CompletedTask,
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NotificationWorker] Failed to process {ItemType}", item.GetType().Name);
            }
        }
    }
}
