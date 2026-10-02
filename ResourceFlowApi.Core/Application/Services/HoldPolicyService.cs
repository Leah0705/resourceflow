using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Pre-flight validation for resource holds. Enforces the same open-hours / walk-in /
/// pause / past-date policy as <c>BookingService.CreateBookingAsync</c>, then checks
/// for a conflicting confirmed booking. Extracted from <c>HoldsController</c> so the
/// controller is a thin HTTP mapper. Stateless apart from its repository dependencies;
/// safe to share one Scoped instance per request.
/// </summary>
public sealed class HoldPolicyService(
    IVenueRepository venueRepository,
    IBookingRepository bookingRepository) : IHoldPolicyService
{
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly IBookingRepository _bookingRepository = bookingRepository;

    public async Task<HoldPolicyResult> ValidateAsync(int venueId, int resourceId, DateTime requestedDate, int partySize)
    {
        HoldPolicyResult venuePolicy = await ValidateVenuePolicyAsync(venueId, requestedDate);
        if (venuePolicy.Status != HoldPolicyStatus.Eligible)
        {
            return venuePolicy;
        }

        DateTime bookingDate = venuePolicy.BookingDate;
        Venue venue = venuePolicy.Venue!;
        bool walkInOnly = venue.Sections?
            .SelectMany(s => s.Resources ?? [])
            .Any(t => t.Id == resourceId && t.WalkInOnly) == true;
        if (walkInOnly)
        {
            return HoldPolicyResult.Booked("This resource is kept for walk-ins and can't be booked online.", ErrorCodes.ResourceWalkInOnly);
        }

        // Existing confirmed booking on the same resource.
        bool alreadyBooked = await _bookingRepository.IsResourceBookedOnDateAsync(
            resourceId, bookingDate, BookingDuration.For(venue, partySize));
        if (alreadyBooked)
        {
            return HoldPolicyResult.Booked("This resource is already booked for that time.", ErrorCodes.BookingResourceConflict);
        }

        return venuePolicy;
    }

    public async Task<HoldPolicyResult> ValidateAnyResourceAsync(int venueId, DateTime requestedDate)
    {
        // Same venue-level policy gates as ValidateAsync, but no per-resource booking
        // check — the caller will compute the candidate pool and HoldService.PlaceAutoHold
        // will atomically pick the first free resource under its lock.
        return await ValidateVenuePolicyAsync(venueId, requestedDate);
    }

    /// <summary>
    /// Shared venue-level policy checks (1–6 of the original ValidateAsync): fetch +
    /// timezone-normalize + past-date + pause + walk-in + operating hours. Returns
    /// <see cref="HoldPolicyStatus.Eligible"/> with the resolved venue and UTC booking
    /// date, or the appropriate rejection status. The per-resource booking check is left to
    /// the callers because auto-assign needs to evaluate it per-candidate, not upfront.
    /// </summary>
    private async Task<HoldPolicyResult> ValidateVenuePolicyAsync(int venueId, DateTime requestedDate)
    {
        // 1. Fetch venue first to get its timezone.
        Venue? venue = await _venueRepository.GetByIdAsync(venueId);
        if (venue == null)
        {
            return HoldPolicyResult.NotFound();
        }

        // 2. Normalize date: if Unspecified, treat as venue local and convert to UTC.
        DateTime bookingDate = TimeZoneHelper.ConvertLocalToUtc(requestedDate, venue.Timezone);

        // 3. Past-date guard (same 5-min tolerance as booking create/cancel).
        if (bookingDate < DateTime.UtcNow.AddMinutes(-Booking.CancellationGraceMinutes))
        {
            return HoldPolicyResult.Rejected("Cannot hold a resource for a past time.", ErrorCodes.BookingPastDate);
        }

        // 4. Pause window — scoped to the slots inside it, not to every future date.
        if (venue.IsPausedFor(bookingDate))
        {
            PauseHelper.Rejection pause = PauseHelper.RejectionFor(venue);
            return HoldPolicyResult.Rejected(pause.Message, pause.Code, pause.Args);
        }

        // 5. Walk-in-only policy (location-wide or per ISO day).
        if (venue.IsWalkInOnlyAt(bookingDate))
        {
            return HoldPolicyResult.Rejected(
                venue.WalkInOnly
                    ? "This location accepts walk-ins only and does not take online bookings."
                    : "This location accepts walk-ins only on the selected day.",
                venue.WalkInOnly ? ErrorCodes.BookingWalkInOnly : ErrorCodes.BookingWalkInOnlyToday);
        }

        // 6. Operating hours / open days.
        if (!venue.IsOpenAt(bookingDate))
        {
            return HoldPolicyResult.Rejected("The venue is closed at the requested time.", ErrorCodes.VenueClosedAtTime);
        }

        return HoldPolicyResult.Eligible(venue, bookingDate);
    }
}
