using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(int id);
    Task<Booking?> GetByRefAsync(string bookingRef);
    Task<IEnumerable<Booking>> GetBookingsByVenueIdAsync(int venueId);
    Task<Booking> AddAsync(Booking booking);
    Task<Booking> UpdateAsync(Booking booking);
    Task DeleteAsync(int id);
    /// <summary>
    /// Returns true if a confirmed booking exists for this resource whose occupancy window overlaps
    /// [<paramref name="bookingDate"/>, <paramref name="bookingDate"/> + <paramref name="durationMinutes"/>).
    /// For existing bookings without an explicit <c>EndTime</c>, <paramref name="durationMinutes"/> is
    /// also used as the fallback occupancy window.
    /// </summary>
    Task<bool> IsResourceBookedOnDateAsync(int resourceId, DateTime bookingDate, int durationMinutes = 60);

    /// <summary>
    /// The conflict check every booking path uses, because a resource and the group it belongs to
    /// reserve the same physical space. A <paramref name="resourceId"/> is reserved by a booking
    /// on that resource <em>or</em> by a booking on its group; a <paramref name="resourceGroupId"/> is
    /// reserved by a booking on the group <em>or</em> on any member individually.
    /// <para>
    /// A group booking stores <c>ResourceId = null</c>, so
    /// <see cref="IsResourceBookedOnDateAsync"/> cannot see it — reaching for that one instead is
    /// how the same resource gets booked twice.
    /// </para>
    /// </summary>
    /// <seealso>BookingServiceTests.CreateBookingAsync_GroupBooking_RejectsWhenMemberIsBooked</seealso>
    /// <seealso>BookingServiceTests.CreateBookingAsync_SingleResource_RejectsWhenItsGroupAlreadyBooked</seealso>
    /// <param name="resourceId"></param>
    /// <param name="resourceGroupId"></param>
    /// <param name="bookingDate"></param>
    /// <param name="durationMinutes"></param>
    /// <param name="excludeBookingId">
    /// The booking being moved, which must not be found conflicting with itself. Null when
    /// checking a booking that does not exist yet.
    /// </param>
    Task<bool> IsUnitBookedOnDateAsync(
        int? resourceId,
        int? resourceGroupId,
        DateTime bookingDate,
        int durationMinutes = 60,
        int? excludeBookingId = null);
    /// <summary>Returns all non-cancelled bookings for a specific venue and local date.</summary>
    Task<IEnumerable<Booking>> GetActiveBookingsForDateAsync(int venueId, DateTime bookingDate);

    // ── Bundle 2 additions ───────────────────────────────────────────────────

    /// <summary>Finds a booking by id with NO navigation properties loaded. Use <see cref="GetByIdAsync"/> for the eager-loaded graph.</summary>
    Task<Booking?> FindByIdAsync(int id);

    /// <summary>Total non-cancelled bookings, across all venues.</summary>
    Task<int> CountActiveAsync();

    /// <summary>Sum of <see cref="Booking.PartySize"/> across all non-cancelled bookings (0 when none).</summary>
    Task<int> SumActivePartySizeAsync();

    /// <summary>Count of non-cancelled bookings whose <see cref="Booking.Date"/> falls in [<paramref name="startUtc"/>, <paramref name="endUtc"/>).</summary>
    Task<int> CountActiveByDayAsync(DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Non-cancelled bookings for a venue whose occupancy window intersects the moment
    /// <paramref name="nowUtc"/>. Navigation properties (Venue/Section/Resource) are eager-loaded.
    /// Used by <c>AdminService.ExtendAllActiveBookingsAsync</c>.
    /// </summary>
    Task<List<Booking>> GetInProgressForVenueAsync(int venueId, DateTime nowUtc, int defaultDurationMinutes);

    /// <summary>
    /// Non-cancelled bookings for a venue whose <see cref="Booking.Date"/> is in
    /// [<paramref name="startUtc"/>, <paramref name="endUtc"/>) and whose navigation properties
    /// are eager-loaded. Used by the overview "today" list.
    /// </summary>
    Task<List<Booking>> GetForVenueInUtcRangeAsync(int venueId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// True if any other booking on the same resource overlaps the window
    /// [<paramref name="newStart"/>, <paramref name="newEnd"/>). Existing bookings without an
    /// explicit <see cref="Booking.EndTime"/> use <c>Booking.Date + <paramref name="fallbackDurationMinutes"/></c>
    /// as their end. Pass <paramref name="excludeBookingId"/> to skip a booking being updated.
    /// </summary>
    Task<bool> HasConflictAsync(int? resourceId, DateTime newStart, DateTime newEnd, int fallbackDurationMinutes, int? excludeBookingId = null);

    /// <summary>Distinct count of resources with at least one non-cancelled booking in the UTC window — used by the capacity notification.</summary>
    Task<int> CountDistinctBookedResourcesAsync(int venueId, DateTime startUtc, DateTime endUtc);

    /// <summary>Adds multiple bookings to the change tracker (caller is responsible for SaveChanges).</summary>
    Task AddRangeAsync(IEnumerable<Booking> bookings);

    /// <summary>Removes multiple bookings (caller is responsible for SaveChanges).</summary>
    void RemoveRange(IEnumerable<Booking> bookings);

    /// <summary>
    /// Flushes all pending changes on the underlying DbContext. Exposed so services that mutate
    /// multiple tracked entities (loaded via read methods) can persist them in a single round-trip,
    /// mirroring the prior <c>SaveChangesAsync</c>-once-per-method behavior.
    /// </summary>
    Task SaveChangesAsync();

    /// <summary>
    /// All bookings whose SectionId matches, OR whose ResourceId is in <paramref name="resourceIds"/> — used by
    /// VenueManagementService.DeleteSectionAsync to FK-null affected bookings before the cascade.
    /// </summary>
    Task<List<Booking>> GetBySectionOrResourcesAsync(int sectionId, IReadOnlyList<int> resourceIds);

    /// <summary>All bookings assigned to a resource — used by VenueManagementService.DeleteResourceAsync to FK-null them.</summary>
    Task<List<Booking>> GetByResourceAsync(int resourceId);

    /// <summary>
    /// Count of non-cancelled <b>future</b> bookings (start &gt;= <paramref name="nowUtc"/>) assigned to a specific
    /// resource — used by the resource-delete impact read to show the admin how many upcoming bookings would lose their
    /// resource reference. Distinct from <see cref="GetByResourceAsync"/>, which returns the full set (incl. past/cancelled)
    /// for the actual FK-nulling on delete.
    /// </summary>
    Task<int> CountFutureByResourceAsync(int resourceId, DateTime nowUtc);

    /// <summary>
    /// Count of non-cancelled <b>future</b> bookings (start &gt;= <paramref name="nowUtc"/>) whose SectionId matches
    /// OR whose ResourceId is in <paramref name="resourceIds"/> — used by the section-delete impact read (same membership
    /// rule as <see cref="GetBySectionOrResourcesAsync"/>, restricted to upcoming + non-cancelled).
    /// </summary>
    Task<int> CountFutureBySectionOrResourcesAsync(int sectionId, IReadOnlyList<int> resourceIds, DateTime nowUtc);

    /// <summary>
    /// Count of non-cancelled <b>future</b> bookings (start &gt;= <paramref name="nowUtc"/>) reserving any of
    /// <paramref name="resourceGroupIds"/>. A group booking stores ResourceId = null, so it is invisible to
    /// <see cref="CountFutureByResourceAsync"/>/<see cref="CountFutureBySectionOrResourcesAsync"/> — the delete-impact
    /// reads add this in so deleting a combinable resource doesn't report "0 bookings affected" while a merged-resource
    /// party is on the books for it.
    /// </summary>
    Task<int> CountFutureByResourceGroupsAsync(IReadOnlyList<int> resourceGroupIds, DateTime nowUtc);

    /// <summary>All bookings whose ResourceGroupId matches — used by VenueManagementService.DeleteResourceGroupAsync to FK-null them.</summary>
    Task<List<Booking>> GetByResourceGroupAsync(int resourceGroupId);

    /// <summary>
    /// Non-cancelled bookings for a venue starting at or after <paramref name="nowUtc"/>, oldest
    /// first and with no navigation properties loaded — the set the schedule-conflict read re-evaluates
    /// against the location's current opening hours and walk-in policy.
    /// </summary>
    Task<List<Booking>> GetFutureForVenueAsync(int venueId, DateTime nowUtc);

    /// <summary>
    /// No-show bookings per customer email across every location, keyed by the lower-cased email.
    /// Emails with none are absent.
    /// </summary>
    Task<Dictionary<string, int>> CountNoShowsByEmailAsync(IEnumerable<string> emails);
}
