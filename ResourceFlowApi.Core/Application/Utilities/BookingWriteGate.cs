namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Serializes the check-and-create path across guest, staff and waitlist bookings in one
/// application instance. The existing hold store is also in memory, so this preserves the
/// deployment's single-instance consistency model without changing the database schema.
/// </summary>
internal static class BookingWriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        await Gate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            Gate.Release();
        }
    }
}
