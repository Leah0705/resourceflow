using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AdminServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerRestoreTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerSectionsReorderTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerUpdateTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerEmailTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerLookupTests")]
[ExternalAccessAllowed]
internal class BookingFilterRepository(AppDbContext db) : IBookingFilterRepository
{
    private readonly AppDbContext _db = db;

    private static string NormalizeStatus(string status) => status.ToLowerInvariant() switch
    {
        "upcoming" => "active",
        "past" => "past",
        "cancelled" => "cancelled",
        "active" => "active",
        "all" => "all",
        "noshow" => "noshow",
        _ => "active",
    };

    /// <summary>
    /// The EF-translatable form of <see cref="Booking.IsPastForGrid"/>: a slot is past once it
    /// started more than <see cref="Booking.GridGraceMinutes"/> ago or has been finished or
    /// no-showed, and "active" is everything else that is not cancelled.
    /// </summary>
    /// <seealso>AdminServiceTests.GetBookingsAsync_ListsAFinishedSlotAsPast_InsideTheGridGrace</seealso>
    private static IQueryable<Booking> WhereStatus(IQueryable<Booking> q, string normalized, DateTime cutoff)
        => normalized switch
        {
            "cancelled" => q.Where(b => b.IsCancelled),
            "past" => q.Where(b => !b.IsCancelled
                && (b.Date < cutoff || b.Status == BookingStatus.Finished || b.Status == BookingStatus.NoShow)),
            "noshow" => q.Where(b => !b.IsCancelled && b.Status == BookingStatus.NoShow),
            "all" => q,
            _ => q.Where(b => !b.IsCancelled
                && b.Date >= cutoff && b.Status != BookingStatus.Finished && b.Status != BookingStatus.NoShow),
        };

    public async Task<List<Booking>> QueryAsync(BookingFilter filter)
    {
        IQueryable<Booking> q = _db.Bookings
            .Include(b => b.Venue)
            .Include(b => b.Section)
            .Include(b => b.Resource)
            .Include(b => b.ResourceGroup!).ThenInclude(g => g.Members).ThenInclude(m => m.Resource)
            .AsQueryable();

        DateTime nowUtc = DateTime.UtcNow;
        string normalized = NormalizeStatus(filter.Status);

        // Grid view logic: if a date is explicitly provided, we usually want all bookings for that day
        // unless a specific status (like cancelled) is requested.
        bool isGridMode = filter.BookingDate.HasValue && normalized == "active";

        if (filter.VenueId.HasValue)
        {
            q = q.Where(b => b.VenueId == filter.VenueId.Value);
            Venue? venue = await _db.Venues.FindAsync(filter.VenueId.Value);
            string tz = venue?.Timezone ?? "UTC";

            DateTime cutoff = nowUtc.AddMinutes(-Booking.GridGraceMinutes);

            if (isGridMode)
            {
                // In grid mode for a specific date, show everything non-cancelled for that day
                q = q.Where(b => !b.IsCancelled);
            }
            else
            {
                q = WhereStatus(q, normalized, cutoff);
            }

            if (filter.BookingDate.HasValue)
            {
                (DateTime start, DateTime end) = TimeZoneHelper.GetUtcRangeForLocalDay(filter.BookingDate.Value, tz);
                q = q.Where(b => b.Date >= start && b.Date < end);
            }
        }
        else
        {
            if (isGridMode)
            {
                // In grid mode for a specific date, show everything non-cancelled for that day
                q = q.Where(b => !b.IsCancelled);
            }
            else
            {
                q = WhereStatus(q, normalized, nowUtc.AddMinutes(-Booking.GridGraceMinutes));
            }

            if (filter.BookingDate.HasValue)
            {
                DateTime dayStart = filter.BookingDate.Value.Date;
                DateTime nextDayStart = dayStart.AddDays(1);
                q = q.Where(b => b.Date >= dayStart && b.Date < nextDayStart);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Email))
        {
            // SQLite EF Core cannot translate StringComparison overloads — use ToLower for case-insensitive LIKE
            string normalizedEmail = filter.Email.Trim().ToLowerInvariant();
            // EF Core maps ToLower() → SQLite lower(), which is locale-independent at the DB level
#pragma warning disable CA1862, CA1311, CA1304 // ToLower in LINQ-to-EF is intentional (ToLowerInvariant is not translatable)
            q = q.Where(b => b.CustomerEmail != null && b.CustomerEmail.ToLower().Contains(normalizedEmail));
#pragma warning restore CA1862, CA1311, CA1304
        }

        if (!string.IsNullOrWhiteSpace(filter.BookingRef))
        {
            string normalizedRef = filter.BookingRef.Trim().ToLowerInvariant();
#pragma warning disable CA1862, CA1311, CA1304
            q = q.Where(b => b.BookingRef != null && b.BookingRef.ToLower().Contains(normalizedRef));
#pragma warning restore CA1862, CA1311, CA1304
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            string normalizedQuery = filter.Query.Trim().ToLowerInvariant();
#pragma warning disable CA1862, CA1311, CA1304
            q = q.Where(b =>
                (b.CustomerName != null && b.CustomerName.ToLower().Contains(normalizedQuery))
                || (b.CustomerEmail != null && b.CustomerEmail.ToLower().Contains(normalizedQuery))
                || (b.BookingRef != null && b.BookingRef.ToLower().Contains(normalizedQuery)));
#pragma warning restore CA1862, CA1311, CA1304
        }

        return await q
            .OrderBy(b => b.Date)
            .ToListAsync();
    }
}
