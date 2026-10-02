using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

public sealed class AvailabilityService(
    IBookingRepository bookingRepository,
    IVenueRepository venueRepository,
    IHoldService holdService) : IAvailabilityService
{
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly IHoldService _holdService = holdService;

    public async Task<AvailabilityResponseDto> GetAvailabilityAsync(int venueId, DateTime bookingDate, int partySize)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId)
            ?? throw new NotFoundException("Venue not found.") { Code = ErrorCodes.VenueNotFound };

        TimeZoneInfo tz = TimeZoneHelper.Resolve(venue.Timezone);

        IEnumerable<Booking> activeBookings = await _bookingRepository.GetActiveBookingsForDateAsync(venueId, bookingDate);

        // bookingDate arrives as YYYY-MM-DD, already the local date in the venue's
        // timezone. Converting it from UTC would shift midnight into the previous day for any
        // UTC-negative timezone.
        DateTime localDate = bookingDate.Date;
        int isoDay = IsoDay.Of(localDate);

        if (WalkInHelper.IsWalkInOnlyOn(venue, isoDay) || !ServiceWindowHelper.IsOpenOn(venue, isoDay))
        {
            return NoSlots(venueId, bookingDate);
        }

        (DateTime localStart, DateTime localEnd) = ServiceWindowHelper.LocalWindowFor(venue, localDate, isoDay);
        var reservations = new UnitReservations(venue, activeBookings);
        List<Resource> eligibleResources = EligibleResources(venue, partySize);
        List<ResourceGroup> eligibleGroups = EligibleGroups(venue, partySize);
        int durationMinutes = BookingDuration.For(venue, partySize);

        var slots = new List<TimeSlotDto>();
        for (DateTime current = localStart; current < localEnd; current = current.AddMinutes(SlotInterval(venue)))
        {
            DateTime slotUtc = TimeZoneInfo.ConvertTimeToUtc(current, tz);

            // A pause closes the slots inside its window only; later slots — including
            // later today — stay bookable. A slot this party would push over the guest cap is
            // closed the same way.
            if (venue.IsPausedFor(slotUtc) || !HasPacingRoom(venue, activeBookings, slotUtc, partySize))
            {
                slots.Add(ClosedSlot(current));
                continue;
            }

            List<int> availableResourceIds = eligibleResources
                .Where(t => IsResourceFree(t.Id, reservations, slotUtc, durationMinutes))
                .Select(t => t.Id)
                .ToList();
            List<int> availableGroupIds = eligibleGroups
                .Where(g => IsGroupFree(g, reservations, slotUtc, durationMinutes))
                .Select(g => g.Id)
                .ToList();

            slots.Add(new TimeSlotDto
            {
                Time = FormatSlotTime(current),
                IsAvailable = availableResourceIds.Count > 0 || availableGroupIds.Count > 0,
                AvailableResourceIds = availableResourceIds,
                AvailableGroupIds = availableGroupIds,
                Category = GetCategory(current)
            });
        }

        return new AvailabilityResponseDto
        {
            VenueId = venueId,
            Date = bookingDate,
            Slots = slots
        };
    }

    private static AvailabilityResponseDto NoSlots(int venueId, DateTime bookingDate) => new()
    {
        VenueId = venueId,
        Date = bookingDate,
        Slots = new List<TimeSlotDto>()
    };

    private static TimeSlotDto ClosedSlot(DateTime local) => new()
    {
        Time = FormatSlotTime(local),
        IsAvailable = false,
        AvailableResourceIds = new List<int>(),
        Category = GetCategory(local)
    };

    private static string FormatSlotTime(DateTime local)
        => local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A zero or negative stored interval would spin the slot loop forever. Validation keeps
    /// the column to 15/30/60, so this only ever catches a stale or hand-edited row.
    /// </summary>
    internal static int SlotInterval(Venue venue)
        => venue.BookingSlotIntervalMinutes > 0 ? venue.BookingSlotIntervalMinutes : 30;

    /// <summary>Whether this party still fits under the guest cap in the slot starting at <paramref name="slotUtc"/>.</summary>
    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_ClosesASlot_ThePartyWouldTakeOverTheGuestCap</seealso>
    private static bool HasPacingRoom(Venue venue, IEnumerable<Booking> activeBookings, DateTime slotUtc, int partySize)
        => (GuestPacing.Remaining(venue, activeBookings, slotUtc) ?? int.MaxValue) >= partySize;

    /// <summary>Resources that can fit the party online. Walk-in-only resources are never offered.</summary>
    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_NeverOffersAWalkInOnlyResource</seealso>
    private static List<Resource> EligibleResources(Venue venue, int partySize)
        => venue.Sections
            ?.SelectMany(s => s.Resources ?? new List<Resource>())
            .Where(t => t != null && !t.WalkInOnly && venue.CanFit(t.Capacity, partySize))
            .ToList() ?? new List<Resource>();

    /// <summary>
    /// Combinable groups are bookable units alongside the individual resources. Member resources
    /// stay in the eligible set — grouping a resource does not stop it being booked on its own —
    /// so the mutual exclusion between a member and its group is resolved per slot by
    /// <see cref="UnitReservations"/> rather than by hiding the members here. A group holding a
    /// walk-in-only resource is left out, or booking it online would take that resource.
    /// </summary>
    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_StillOffersGroupMembers_Individually</seealso>
    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_RemovesGroupMember_WhenReservedByItsGroupBooking</seealso>
    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_NeverOffersAGroupWithAWalkInOnlyMember</seealso>
    private static List<ResourceGroup> EligibleGroups(Venue venue, int partySize)
        => (venue.Groups ?? Enumerable.Empty<ResourceGroup>())
            .Where(g => !g.HasWalkInOnlyMember() && venue.CanFit(g.CombinedCapacity, partySize))
            .ToList();

    /// <seealso>AvailabilityServiceTests.GetAvailabilityAsync_RefusesALargePartyAGapThatOnlyFitsTheSmallerDurationRule</seealso>
    private bool IsResourceFree(int resourceId, UnitReservations reservations, DateTime slotUtc, int durationMinutes)
        => !reservations.IsResourceReserved(resourceId, slotUtc, slotUtc.AddMinutes(durationMinutes))
            && !_holdService.IsResourceHeld(resourceId, slotUtc, durationMinutes: durationMinutes);

    private bool IsGroupFree(ResourceGroup group, UnitReservations reservations, DateTime slotUtc, int durationMinutes)
        => !reservations.IsGroupReserved(group, slotUtc, slotUtc.AddMinutes(durationMinutes))
            && group.Members.All(m => !_holdService.IsResourceHeld(m.ResourceId, slotUtc, durationMinutes: durationMinutes));

    /// <summary>
    /// Indexes the day's bookings so each slot can be answered without rescanning them. A
    /// group booking stores <c>ResourceId = null</c>, so it is invisible to a resource-keyed lookup;
    /// its members' resource ids are resolved up front, or a resource reserved by its group's
    /// booking would be advertised as available.
    /// </summary>
    private sealed class UnitReservations
    {
        private readonly int _defaultDurationMinutes;
        private readonly Dictionary<int, List<Booking>> _byResource;
        private readonly List<Booking> _groupBookings;
        private readonly List<(int ResourceId, Booking Booking)> _resourcesHeldByGroupBookings;

        public UnitReservations(Venue venue, IEnumerable<Booking> activeBookings)
        {
            _defaultDurationMinutes = venue.DefaultBookingDurationMinutes;

            _byResource = activeBookings
                .Where(b => b.ResourceId.HasValue)
                .GroupBy(b => b.ResourceId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            _groupBookings = activeBookings.Where(b => b.ResourceGroupId.HasValue).ToList();

            Dictionary<int, HashSet<int>> memberResourceIdsByGroup = (venue.Groups ?? Enumerable.Empty<ResourceGroup>())
                .ToDictionary(g => g.Id, g => g.Members.Select(m => m.ResourceId).ToHashSet());

            _resourcesHeldByGroupBookings = _groupBookings
                .Where(b => memberResourceIdsByGroup.ContainsKey(b.ResourceGroupId!.Value))
                .SelectMany(b => memberResourceIdsByGroup[b.ResourceGroupId!.Value].Select(id => (id, b)))
                .ToList();
        }

        public bool IsResourceReserved(int resourceId, DateTime slotUtc, DateTime slotEndUtc)
        {
            if (_byResource.TryGetValue(resourceId, out List<Booking>? resourceBookings)
                && resourceBookings.Any(b => Overlaps(b, slotUtc, slotEndUtc)))
            {
                return true;
            }

            return _resourcesHeldByGroupBookings.Any(x => x.ResourceId == resourceId && Overlaps(x.Booking, slotUtc, slotEndUtc));
        }

        public bool IsGroupReserved(ResourceGroup group, DateTime slotUtc, DateTime slotEndUtc)
            => _groupBookings.Any(b => b.ResourceGroupId == group.Id && Overlaps(b, slotUtc, slotEndUtc))
                || group.Members.Any(m => IsResourceReserved(m.ResourceId, slotUtc, slotEndUtc));

        private bool Overlaps(Booking booking, DateTime slotUtc, DateTime slotEndUtc)
            => booking.Date < slotEndUtc
                && (booking.EndTime ?? booking.Date.AddMinutes(_defaultDurationMinutes)) > slotUtc;
    }

    // Keep the period filter independent of venue type and opening hours. Every slot belongs
    // to exactly one half of the local day, including early-morning and late-night slots.
    private static string GetCategory(DateTime time)
        => time.Hour < 12 ? "AM" : "PM";
}
