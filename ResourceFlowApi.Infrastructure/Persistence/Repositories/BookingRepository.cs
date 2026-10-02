using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories
{
    [OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AvailabilityServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AdminServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.HoldPolicyServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.NotificationServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingNotificationServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalletPassServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.VenueManagementServiceTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalkInTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.ResourceAutoAssignerTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerRestoreTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerSectionsReorderTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerUpdateTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerEmailTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerLookupTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Integration.RepositoryTests")]
    [OnlyAccessibleBy("ResourceFlowApi.Tests.Services.GuestReminderServiceTests")]
    [ExternalAccessAllowed]
    internal class BookingRepository(AppDbContext db) : IBookingRepository
    {
        private readonly AppDbContext _db = db;

        public async Task<Booking> AddAsync(Booking booking)
        {
            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync();
            return booking;
        }

        public async Task<Booking?> GetByIdAsync(int id)
        {
            return await _db.Bookings
                .Include(b => b.Resource)
                .Include(b => b.Section)
                .Include(b => b.ResourceGroup)!
                    .ThenInclude(g => g.Members)
                        .ThenInclude(m => m.Resource)
                .Include(b => b.Venue)
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<Booking?> GetByRefAsync(string bookingRef)
        {
            string normalized = bookingRef.Trim().ToLowerInvariant();
            return await _db.Bookings
                .Include(b => b.Resource)
                .Include(b => b.Section)
                .Include(b => b.ResourceGroup)!
                    .ThenInclude(g => g.Members)
                        .ThenInclude(m => m.Resource)
                .Include(b => b.Venue)
                // Refs are minted lowercase, so matching case-insensitively costs nothing and
                // spares a guest whose keyboard or mail client capitalised the ref they pasted.
                // Still an exact match: a prefix match would let anyone enumerate other bookings.
#pragma warning disable CA1862, CA1311, CA1304 // ToLower in LINQ-to-EF is intentional (ToLowerInvariant is not translatable)
                .FirstOrDefaultAsync(b => b.BookingRef.ToLower() == normalized);
#pragma warning restore CA1862, CA1311, CA1304
        }

        public async Task<IEnumerable<Booking>> GetBookingsByVenueIdAsync(int venueId)
        {
            return await _db.Bookings
                .Include(b => b.Resource)
                .Include(b => b.Section)
                .Include(b => b.ResourceGroup)!
                    .ThenInclude(g => g.Members)
                        .ThenInclude(m => m.Resource)
                .Include(b => b.Venue)
                .Where(b => b.Venue.Id == venueId)
                .ToListAsync();
        }

        public async Task<Booking> UpdateAsync(Booking booking)
        {
            _db.Entry(booking).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return booking;
        }

        public async Task DeleteAsync(int id)
        {
            Booking? booking = await _db.Bookings.FindAsync(id);
            if (booking != null)
            {
                _db.Bookings.Remove(booking);
                await _db.SaveChangesAsync();
            }
        }

        public async Task<Dictionary<string, int>> CountNoShowsByEmailAsync(IEnumerable<string> emails)
        {
            List<string> lowered = emails.Select(e => e.Trim().ToLowerInvariant()).Distinct().ToList();
            if (lowered.Count == 0)
            {
                return [];
            }

#pragma warning disable CA1862, CA1311, CA1304 // ToLower in LINQ-to-EF is intentional (ToLowerInvariant is not translatable)
            return await _db.Bookings
                .Where(b => b.Status == BookingStatus.NoShow && !b.IsCancelled && b.CustomerEmail != null
                    && lowered.Contains(b.CustomerEmail.Trim().ToLower()))
                .GroupBy(b => b.CustomerEmail!.Trim().ToLower())
                .ToDictionaryAsync(g => g.Key, g => g.Count());
#pragma warning restore CA1862, CA1311, CA1304
        }

        public async Task<bool> IsResourceBookedOnDateAsync(int resourceId, DateTime bookingDate, int durationMinutes = 60)
        {
            DateTime newStart = bookingDate.ToUniversalTime();
            DateTime newEnd = newStart.AddMinutes(durationMinutes);
            DateTime thresholdStart = newStart.AddMinutes(-durationMinutes); // For legacy bookings without EndTime

            // Half-open overlap: existing starts before the new window ends, and ends after it
            // begins. A booking that ends exactly when this one starts is not a conflict.
            return await _db.Bookings.AnyAsync(b =>
                b.ResourceId == resourceId &&
                !b.IsCancelled &&
                b.Date < newEnd &&
                (b.EndTime != null ? b.EndTime > newStart : b.Date > thresholdStart));
        }

        /// <summary>
        /// Group-aware conflict check. Resolves the set of physical resource ids and group ids that count
        /// as "the unit being booked", then returns true if any non-cancelled booking overlapping the
        /// window reserves one of those resources (directly or via a group), or one of those groups.
        /// See <see cref="IBookingRepository.IsUnitBookedOnDateAsync"/> for the contract.
        /// </summary>
        public async Task<bool> IsUnitBookedOnDateAsync(
            int? resourceId,
            int? resourceGroupId,
            DateTime bookingDate,
            int durationMinutes = 60,
            int? excludeBookingId = null)
        {
            DateTime newStart = bookingDate.ToUniversalTime();
            DateTime newEnd = newStart.AddMinutes(durationMinutes);
            DateTime thresholdStart = newStart.AddMinutes(-durationMinutes);

            // Resolve every resource id that counts as reserved by this unit. Booking a resource also
            // reserves its group's other members (they can't be combined while one is taken);
            // booking a group reserves all of its members.
            var reservedResourceIds = new HashSet<int>();

            if (resourceGroupId.HasValue)
            {
                foreach (ResourceGroupMembership m in await _db.ResourceGroupMemberships
                    .Where(m => m.ResourceGroupId == resourceGroupId.Value).ToListAsync())
                {
                    reservedResourceIds.Add(m.ResourceId);
                }
            }

            if (resourceId.HasValue)
            {
                reservedResourceIds.Add(resourceId.Value);
            }

            if (reservedResourceIds.Count == 0)
            {
                return false;
            }

            // Expand to every group that contains ANY reserved resource — booking a resource that shares a
            // group with another resource (even via a different group) conflicts with that group's
            // bookings too, because those bookings reserve the shared physical resource. This is what
            // makes a group booking (ResourceId = null) visible: it's matched on its ResourceGroupId here.
            var reservedGroupIds = new HashSet<int>(
                await _db.ResourceGroupMemberships
                    .Where(m => reservedResourceIds.Contains(m.ResourceId))
                    .Select(m => m.ResourceGroupId)
                    .Distinct()
                    .ToListAsync());

            if (resourceGroupId.HasValue)
            {
                reservedGroupIds.Add(resourceGroupId.Value);
            }

            return await _db.Bookings.AnyAsync(b =>
                !b.IsCancelled &&
                (excludeBookingId == null || b.Id != excludeBookingId.Value) &&
                b.Date < newEnd &&
                (b.EndTime != null ? b.EndTime > newStart : b.Date > thresholdStart) &&
                ((b.ResourceId.HasValue && reservedResourceIds.Contains(b.ResourceId.Value)) ||
                 (b.ResourceGroupId.HasValue && reservedGroupIds.Contains(b.ResourceGroupId.Value))));
        }

        public async Task<IEnumerable<Booking>> GetActiveBookingsForDateAsync(int venueId, DateTime bookingDate)
        {
            // Define a range in UTC that is guaranteed to cover the entire day regardless of timezone.
            // A 48-hour window centered on the UTC date is safe.
            DateTime start = bookingDate.Date.AddDays(-1);
            DateTime end = bookingDate.Date.AddDays(2);

            return await _db.Bookings
                .Where(b => b.VenueId == venueId && !b.IsCancelled && b.Date >= start && b.Date < end)
                .ToListAsync();
        }

        // ── Bundle 2 additions ─────────────────────────────────────────────────────

        public async Task<Booking?> FindByIdAsync(int id)
        {
            return await _db.Bookings.FindAsync(id);
        }

        public async Task<int> CountActiveAsync()
        {
            return await _db.Bookings.CountAsync(b => !b.IsCancelled);
        }

        public async Task<int> SumActivePartySizeAsync()
        {
            return await _db.Bookings.Where(b => !b.IsCancelled).SumAsync(b => (int?)b.PartySize) ?? 0;
        }

        public async Task<int> CountActiveByDayAsync(DateTime startUtc, DateTime endUtc)
        {
            return await _db.Bookings.CountAsync(b => !b.IsCancelled && b.Date >= startUtc && b.Date < endUtc);
        }

        public async Task<List<Booking>> GetInProgressForVenueAsync(int venueId, DateTime nowUtc, int defaultDurationMinutes)
        {
            return await _db.Bookings
                .Include(b => b.Venue)
                .Include(b => b.Section)
                .Include(b => b.Resource)
                .Include(b => b.ResourceGroup)!
                    .ThenInclude(g => g.Members)
                        .ThenInclude(m => m.Resource)
                .Where(b => b.VenueId == venueId &&
                            !b.IsCancelled &&
                            b.Date <= nowUtc &&
                            (b.EndTime.HasValue ? b.EndTime.Value > nowUtc : b.Date.AddMinutes(defaultDurationMinutes) > nowUtc))
                .ToListAsync();
        }

        public async Task<List<Booking>> GetForVenueInUtcRangeAsync(int venueId, DateTime startUtc, DateTime endUtc)
        {
            return await _db.Bookings
                .Include(b => b.Venue)
                .Include(b => b.Section)
                .Include(b => b.Resource)
                .Include(b => b.ResourceGroup)!
                    .ThenInclude(g => g.Members)
                        .ThenInclude(m => m.Resource)
                .Where(b => b.VenueId == venueId &&
                            b.Date >= startUtc && b.Date < endUtc &&
                            !b.IsCancelled)
                .OrderBy(b => b.Date)
                .ToListAsync();
        }

        public async Task<bool> HasConflictAsync(int? resourceId, DateTime newStart, DateTime newEnd, int fallbackDurationMinutes, int? excludeBookingId = null)
        {
            return await _db.Bookings.AnyAsync(b =>
                b.ResourceId == resourceId &&
                !b.IsCancelled &&
                (excludeBookingId == null || b.Id != excludeBookingId.Value) &&
                b.Date < newEnd &&
                (b.EndTime != null ? b.EndTime > newStart : b.Date.AddMinutes(fallbackDurationMinutes) > newStart));
        }

        public async Task<int> CountDistinctBookedResourcesAsync(int venueId, DateTime startUtc, DateTime endUtc)
        {
            return await _db.Bookings
                .Where(b => b.VenueId == venueId && !b.IsCancelled && b.ResourceId != null && b.Date >= startUtc && b.Date < endUtc)
                .Select(b => b.ResourceId)
                .Distinct()
                .CountAsync();
        }

        public async Task AddRangeAsync(IEnumerable<Booking> bookings)
        {
            await _db.Bookings.AddRangeAsync(bookings);
        }

        public void RemoveRange(IEnumerable<Booking> bookings)
        {
            _db.Bookings.RemoveRange(bookings);
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        public async Task<List<Booking>> GetBySectionOrResourcesAsync(int sectionId, IReadOnlyList<int> resourceIds)
        {
            return await _db.Bookings
                .Where(b => b.SectionId == sectionId || (b.ResourceId != null && resourceIds.Contains(b.ResourceId.Value)))
                .ToListAsync();
        }

        public async Task<List<Booking>> GetByResourceAsync(int resourceId)
        {
            return await _db.Bookings.Where(b => b.ResourceId == resourceId).ToListAsync();
        }

        public async Task<List<Booking>> GetFutureForVenueAsync(int venueId, DateTime nowUtc)
        {
            return await _db.Bookings
                .Where(b => b.VenueId == venueId && !b.IsCancelled && b.Date >= nowUtc)
                .OrderBy(b => b.Date)
                .ToListAsync();
        }

        public async Task<int> CountFutureByResourceAsync(int resourceId, DateTime nowUtc)
        {
            return await _db.Bookings.CountAsync(b => b.ResourceId == resourceId && !b.IsCancelled && b.Date >= nowUtc);
        }

        public async Task<int> CountFutureBySectionOrResourcesAsync(int sectionId, IReadOnlyList<int> resourceIds, DateTime nowUtc)
        {
            return await _db.Bookings.CountAsync(b =>
                !b.IsCancelled
                && b.Date >= nowUtc
                && (b.SectionId == sectionId || (b.ResourceId != null && resourceIds.Contains(b.ResourceId.Value))));
        }

        public async Task<int> CountFutureByResourceGroupsAsync(IReadOnlyList<int> resourceGroupIds, DateTime nowUtc)
        {
            return await _db.Bookings.CountAsync(b =>
                !b.IsCancelled
                && b.Date >= nowUtc
                && b.ResourceGroupId != null
                && resourceGroupIds.Contains(b.ResourceGroupId.Value));
        }

        public async Task<List<Booking>> GetByResourceGroupAsync(int resourceGroupId)
        {
            return await _db.Bookings.Where(b => b.ResourceGroupId == resourceGroupId).ToListAsync();
        }
    }
}
