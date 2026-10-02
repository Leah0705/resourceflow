using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

public class BookingService(
    IBookingRepository bookingRepository,
    IResourceRepository resourceRepository,
    ISectionRepository sectionRepository,
    IVenueRepository venueRepository,
    IHoldService holdService,
    BookingMapper mapper,
    ResourceAutoAssigner autoAssigner,
    IResourceGroupRepository resourceGroupRepository,
    IBookingConfirmationService? confirmationService = null,
    INotificationQueue? notificationQueue = null,
    ICurrentUserService? currentUser = null)
{
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IResourceRepository _resourceRepository = resourceRepository;
    private readonly ISectionRepository _sectionRepository = sectionRepository;
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly IHoldService _holdService = holdService;
    private readonly BookingMapper _mapper = mapper;
    private readonly ResourceAutoAssigner _autoAssigner = autoAssigner;
    private readonly IResourceGroupRepository _resourceGroupRepository = resourceGroupRepository;
    private readonly IBookingConfirmationService? _confirmationService = confirmationService;
    private readonly INotificationQueue? _notificationQueue = notificationQueue;
    private readonly ICurrentUserService _currentUser = currentUser ?? NullCurrentUserService.Instance;

    public virtual async Task<BookingDto> CreateBookingAsync(BookingDto bookingDto)
    {
        CreatedBooking created = await BookingWriteGate.RunAsync(() => CreateBookingCoreAsync(bookingDto));
        await AnnounceBookingAsync(created.Booking, created.Venue);
        return created.Dto;
    }

    private async Task<CreatedBooking> CreateBookingCoreAsync(BookingDto bookingDto)
    {
        Venue venue = await _venueRepository.GetByIdAsync(bookingDto.VenueId)
            ?? throw new NotFoundException("Venue not found.") { Code = ErrorCodes.VenueNotFound };

        // An Unspecified date is the venue's local time; every check below runs on UTC.
        DateTime bookingDate = TimeZoneHelper.ConvertLocalToUtc(bookingDto.Date, venue.Timezone);

        RejectIfClosedToOnlineBookings(venue, bookingDate);
        RejectIfPartySizeOutOfRange(bookingDto.PartySize);
        await RejectIfOverGuestCapAsync(venue, bookingDate, bookingDto.PartySize);

        if (bookingDto.ResourceGroupId.HasValue && bookingDto.ResourceId is null)
        {
            return await CreateGroupBookingAsync(bookingDto, venue, bookingDate);
        }

        if (bookingDto.ResourceId is null || bookingDto.SectionId is null)
        {
            bool ambiguousSelection = bookingDto.ResourceId is null ^ bookingDto.SectionId is null;
            if (ambiguousSelection)
            {
                throw new ValidationException("Specify both ResourceId and SectionId, or neither for auto-assign.") { Code = ErrorCodes.BookingAmbiguousResourceSelection };
            }

            await ResolveAutoAssignAsync(bookingDto, venue, bookingDate);
        }

        // Auto-assign may have landed on a group rather than a resource.
        if (bookingDto.ResourceGroupId.HasValue)
        {
            return await CreateGroupBookingAsync(bookingDto, venue, bookingDate);
        }

        int resourceId = bookingDto.ResourceId!.Value;
        int sectionId = bookingDto.SectionId!.Value;
        int durationMinutes = BookingDuration.For(venue, bookingDto.PartySize);

        bool alreadyBooked = await _bookingRepository.IsUnitBookedOnDateAsync(
            resourceId, resourceGroupId: null, bookingDate, durationMinutes);
        if (alreadyBooked)
        {
            throw new ConflictException("This resource is already booked for that time.") { Code = ErrorCodes.BookingResourceConflict };
        }

        bool heldByOther = _holdService.IsResourceHeld(
            resourceId, bookingDate, excludeHoldId: bookingDto.HoldId,
            durationMinutes: durationMinutes);
        if (heldByOther)
        {
            throw new ConflictException("This resource is currently being held by another user. Please try again shortly.") { Code = ErrorCodes.BookingResourceHeld };
        }

        Resource? resource = await _resourceRepository.GetByIdAsync(resourceId);
        RejectIfResourceCannotFit(resource, venue, bookingDto.PartySize);
        if (resource?.WalkInOnly == true)
        {
            throw WalkInOnlyResource();
        }

        Booking booking = _mapper.ToEntity(bookingDto);
        booking.Date = bookingDate;
        booking.BookingRef = BookingRefFactory.GenerateFor(venue);
        booking.EndTime = bookingDate.AddMinutes(durationMinutes);
        booking.Resource = resource!;
        booking.Section = (await _sectionRepository.GetByIdAsync(sectionId))!;
        booking.Venue = venue;

        Booking newBooking = await _bookingRepository.AddAsync(booking);

        if (!string.IsNullOrEmpty(bookingDto.HoldId))
        {
            _holdService.ReleaseHold(bookingDto.HoldId);
        }

        return new CreatedBooking(_mapper.ToDtoWithGroup(newBooking), newBooking, venue);
    }

    /// <summary>
    /// The three ways a location refuses an online booking for a given slot. Admin-recorded
    /// bookings go through <c>AdminService.CreateBookingAsync</c> and are deliberately exempt
    /// from all of them, so staff can still log a walk-in during a pause.
    /// </summary>
    /// <seealso>BookingServiceTests.CreateBookingAsync_RejectsBooking_InsideThePauseWindow</seealso>
    /// <seealso>BookingServiceTests.CreateBookingAsync_AllowsBooking_AfterThePauseWindow</seealso>
    private static void RejectIfClosedToOnlineBookings(Venue venue, DateTime bookingDate)
    {
        if (bookingDate < DateTime.UtcNow.AddMinutes(-Booking.CancellationGraceMinutes))
        {
            throw new ConflictException("Cannot create a booking in the past.") { Code = ErrorCodes.BookingPastDate };
        }

        if (venue.IsPausedFor(bookingDate))
        {
            PauseHelper.Rejection pause = PauseHelper.RejectionFor(venue);
            throw new ConflictException(pause.Message) { Code = pause.Code, Args = pause.Args };
        }

        if (venue.IsWalkInOnlyAt(bookingDate))
        {
            throw new ConflictException(venue.WalkInOnly
                ? "This location accepts walk-ins only and does not take online bookings."
                : "This location accepts walk-ins only on the selected day. Please choose another day or just come in.")
            { Code = venue.WalkInOnly ? ErrorCodes.BookingWalkInOnly : ErrorCodes.BookingWalkInOnlyToday };
        }
    }

    /// <summary>
    /// Re-checks the guest cap at write time, since availability only reflected it when the guest
    /// loaded the page. A hold reserves a resource, not guest places, so two guests can race for the last
    /// places in a slot and the second is refused here.
    /// </summary>
    /// <seealso>BookingServiceTests.CreateBookingAsync_AcceptsAPartyThatExactlyFillsTheGuestCap</seealso>
    /// <seealso>BookingServiceTests.CreateBookingAsync_RejectsAPartyOneGuestOverTheGuestCap</seealso>
    private async Task RejectIfOverGuestCapAsync(Venue venue, DateTime bookingDate, int partySize)
    {
        if (venue.MaxGuestsPerSlot is not int cap)
        {
            return;
        }

        DateTime slotStart = GuestPacing.SlotStartUtc(venue, bookingDate);
        IEnumerable<Booking> bookings = await _bookingRepository.GetActiveBookingsForDateAsync(venue.Id, bookingDate);
        int remaining = GuestPacing.Remaining(venue, bookings, slotStart)!.Value;
        if (partySize > remaining)
        {
            string time = TimeZoneHelper.ConvertUtcToLocal(slotStart, venue.Timezone)
                .ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            throw new ConflictException($"Only {remaining} more guests can start at {time}.")
            {
                Code = ErrorCodes.BookingPacingFull,
                Args = new Dictionary<string, object> { ["cap"] = cap, ["remaining"] = remaining, ["time"] = time }
            };
        }
    }

    private static ConflictException WalkInOnlyResource()
        => new("This resource is kept for walk-ins and can't be booked online.") { Code = ErrorCodes.ResourceWalkInOnly };

    /// <summary>
    /// Defence in depth behind the DTO's <c>[Range]</c>: without it a party of zero clears the
    /// upper-bound capacity check on any resource and persists.
    /// </summary>
    private static void RejectIfPartySizeOutOfRange(int partySize)
    {
        if (partySize < BookingLimits.MinPartySize || partySize > BookingLimits.MaxPartySize)
        {
            throw new ValidationException(
                $"Party size must be between {BookingLimits.MinPartySize} and {BookingLimits.MaxPartySize}.")
            { Code = ErrorCodes.BookingPartySizeOutOfRange, Args = new Dictionary<string, object> { ["min"] = BookingLimits.MinPartySize, ["max"] = BookingLimits.MaxPartySize } };
        }
    }

    private static void RejectIfResourceCannotFit(Resource? resource, Venue? venue, int partySize)
    {
        if (resource == null)
        {
            return;
        }

        if (partySize > resource.Capacity)
        {
            throw new ConflictException($"This resource has capacity {resource.Capacity}, but {partySize} guests were requested.") { Code = ErrorCodes.ResourceCapacityExceeded, Args = new Dictionary<string, object> { ["capacity"] = resource.Capacity, ["requested"] = partySize } };
        }

        if (venue?.ExceedsOversizeCap(resource.Capacity, partySize) == true)
        {
            throw new ConflictException(
                $"This resource has capacity {resource.Capacity}, which is too large for a party of {partySize}.")
            { Code = ErrorCodes.ResourceOversizeCap, Args = new Dictionary<string, object> { ["capacity"] = resource.Capacity, ["partySize"] = partySize } };
        }
    }

    /// <summary>Best-effort side effects: neither the push nor the email may fail the booking.</summary>
    private async Task AnnounceBookingAsync(Booking booking, Venue venue)
    {
        if (_notificationQueue != null)
        {
            _notificationQueue.EnqueueBookingCreated(booking, venue.Name);
            _notificationQueue.EnqueueCapacityCheck(venue.Id, venue.Name, booking.Date);
        }

        if (_confirmationService != null)
        {
            await _confirmationService.SendConfirmationAsync(booking, venue);
        }
    }

    /// <summary>
    /// Resolves an "Any section" request by writing concrete ids back onto
    /// <paramref name="bookingDto"/>, so everything downstream runs unchanged. A hold the caller
    /// already owns is adopted rather than re-raced; otherwise a fresh one is placed atomically.
    /// The resulting <see cref="BookingDto.HoldId"/> is always the one to release after persisting.
    /// </summary>
    private async Task ResolveAutoAssignAsync(BookingDto bookingDto, Venue venue, DateTime bookingDate)
    {
        if (await TryAdoptExistingHoldAsync(bookingDto, venue, bookingDate))
        {
            return;
        }

        IReadOnlyList<ResourceCandidate> candidates = await _autoAssigner.BuildCandidatesAsync(
            venue, bookingDto.PartySize, bookingDate);

        if (candidates.Count == 0)
        {
            throw new ConflictException("No resources are available for the requested time and party size.") { Code = ErrorCodes.BookingNoResourcesAvailable };
        }

        AutoAssignResult assigned = _holdService.PlaceAutoHold(
            venue.Id,
            candidates,
            bookingDate,
            currentHoldId: bookingDto.HoldId,
            BookingDuration.For(venue, bookingDto.PartySize))
            ?? throw new ConflictException("All suitable resources are currently being held by other users. Please try again shortly.") { Code = ErrorCodes.BookingAllResourcesHeld };

        if (assigned.IsGroup)
        {
            AssignGroup(bookingDto, assigned.ResourceGroupId, assigned.Members, assigned.SectionId);
        }
        else
        {
            bookingDto.ResourceId = assigned.ResourceId;
            bookingDto.SectionId = assigned.SectionId;
        }

        bookingDto.HoldId = assigned.HoldId;
    }

    /// <summary>
    /// A hold was placed atomically, so the unit behind it is the caller's until the booking lands
    /// or the hold expires — adopting it avoids racing for a second one. The hold only guards
    /// against other in-memory holds though, so the database is still asked whether the unit was
    /// booked in the meantime; if it was, the caller falls through to a fresh candidate search.
    /// </summary>
    private async Task<bool> TryAdoptExistingHoldAsync(BookingDto bookingDto, Venue venue, DateTime bookingDate)
    {
        if (string.IsNullOrEmpty(bookingDto.HoldId))
        {
            return false;
        }

        HoldEntry? held = _holdService.GetHold(bookingDto.HoldId);
        if (held is null || held.VenueId != venue.Id)
        {
            return false;
        }

        bool booked = await _bookingRepository.IsUnitBookedOnDateAsync(
            resourceId: held.IsGroup ? null : held.ResourceId,
            resourceGroupId: held.ResourceGroupId,
            bookingDate,
            BookingDuration.For(venue, bookingDto.PartySize));
        if (booked)
        {
            return false;
        }

        if (held.IsGroup)
        {
            AssignGroup(bookingDto, held.ResourceGroupId, held.Members, held.SectionId);
        }
        else
        {
            bookingDto.ResourceId = held.ResourceId;
            bookingDto.SectionId = held.SectionId;
        }

        return true;
    }

    /// <summary>
    /// A group booking reserves the group, not one of its resources, so <c>ResourceId</c> is cleared —
    /// leaving a stale one behind would route the request back down the single-resource path.
    /// </summary>
    private static void AssignGroup(BookingDto bookingDto, int? resourceGroupId, IReadOnlyList<int> memberResourceIds, int sectionId)
    {
        bookingDto.ResourceGroupId = resourceGroupId;
        bookingDto.MemberResourceIds = memberResourceIds;
        bookingDto.SectionId = sectionId;
        bookingDto.ResourceId = null;
    }

    /// <summary>
    /// Persists a booking against a combinable-resource group. The booking is written with
    /// <see cref="Booking.ResourceGroupId"/> set and <see cref="Booking.ResourceId"/> null: it reserves
    /// the group, not one of its resources.
    /// </summary>
    private async Task<CreatedBooking> CreateGroupBookingAsync(BookingDto bookingDto, Venue venue, DateTime bookingDate)
    {
        ResourceGroup group = await _resourceGroupRepository.GetByIdWithMembersAsync(bookingDto.ResourceGroupId!.Value, venue.Id)
            ?? throw new NotFoundException("The selected resource group no longer exists.") { Code = ErrorCodes.ResourceGroupNotFound };

        RejectIfGroupCannotFit(group, venue, bookingDto.PartySize);
        if (group.HasWalkInOnlyMember())
        {
            throw WalkInOnlyResource();
        }

        // Member resources come from the persisted group, never from the request: MemberResourceIds is
        // part of the public POST body, so trusting it would let a caller omit members and skip the
        // hold check for the ones they left out.
        var memberIds = group.Members.Select(m => m.ResourceId).ToList();
        int durationMinutes = BookingDuration.For(venue, bookingDto.PartySize);

        bool groupConflict = await _bookingRepository.IsUnitBookedOnDateAsync(
            resourceId: null, resourceGroupId: group.Id, bookingDate, durationMinutes);
        if (groupConflict)
        {
            throw new ConflictException("One of the combined resources is already booked for that time.") { Code = ErrorCodes.ResourceGroupBookingConflict };
        }

        bool anyMemberHeldByOther = memberIds.Any(id => _holdService.IsResourceHeld(
            id, bookingDate, excludeHoldId: bookingDto.HoldId, durationMinutes: durationMinutes));
        if (anyMemberHeldByOther)
        {
            throw new ConflictException("One of the combined resources is currently being held by another user. Please try again shortly.") { Code = ErrorCodes.ResourceGroupHoldConflict };
        }

        Booking booking = _mapper.ToEntity(bookingDto);
        booking.Date = bookingDate;
        booking.BookingRef = BookingRefFactory.GenerateFor(venue);
        booking.EndTime = bookingDate.AddMinutes(durationMinutes);
        booking.ResourceId = null;
        booking.ResourceGroupId = group.Id;
        booking.ResourceGroup = group;
        // Kept so the admin grid can still group the booking by section.
        booking.SectionId = bookingDto.SectionId
            ?? group.Members.OrderBy(m => m.ResourceId).FirstOrDefault()?.Resource?.SectionId;
        booking.Venue = venue;

        Booking newBooking = await _bookingRepository.AddAsync(booking);

        if (!string.IsNullOrEmpty(bookingDto.HoldId))
        {
            _holdService.ReleaseHold(bookingDto.HoldId);
        }

        return new CreatedBooking(_mapper.ToDtoWithGroup(newBooking), newBooking, venue);
    }

    private sealed record CreatedBooking(BookingDto Dto, Booking Booking, Venue Venue);

    private static void RejectIfGroupCannotFit(ResourceGroup group, Venue venue, int partySize)
    {
        // A group that has lost members — a member resource was deleted — is no longer a combinable
        // unit, and its stored CombinedCapacity no longer describes anything real.
        if (group.Members.Count < 2)
        {
            throw new ConflictException("These resources can no longer be combined. Please pick another time or resource.") { Code = ErrorCodes.ResourceGroupDisbanded };
        }

        if (partySize > group.CombinedCapacity)
        {
            throw new ConflictException(
                $"This group has a combined capacity of {group.CombinedCapacity}, but {partySize} guests were requested.")
            { Code = ErrorCodes.ResourceGroupCapacityExceeded, Args = new Dictionary<string, object> { ["capacity"] = group.CombinedCapacity, ["requested"] = partySize } };
        }

        if (venue.ExceedsOversizeCap(group.CombinedCapacity, partySize))
        {
            throw new ConflictException(
                $"This group has a combined capacity of {group.CombinedCapacity}, which is too large for a party of {partySize}.")
            { Code = ErrorCodes.ResourceGroupOversizeCap, Args = new Dictionary<string, object> { ["capacity"] = group.CombinedCapacity, ["partySize"] = partySize } };
        }
    }

    // Admin-only read (see BookingsController) — the only path here that needs guest-visibility
    // redaction. GetBookingByRefAsync below is the customer's own unauthenticated lookup and must
    // never be redacted, so it deliberately does not route through BookingGuestVisibility.
    public virtual async Task<BookingDto?> GetBookingByIdAsync(int id)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(id);
        return booking == null ? null : BookingGuestVisibility.Apply(_mapper.ToDtoWithGroup(booking), _currentUser);
    }

    public virtual async Task<BookingDto?> GetBookingByRefAsync(string bookingRef)
    {
        Booking? booking = await _bookingRepository.GetByRefAsync(bookingRef);
        return booking == null ? null : _mapper.ToDtoWithGroup(booking);
    }

    // Admin-only read (see BookingsController) — see the note on GetBookingByIdAsync above.
    public virtual async Task<IEnumerable<BookingDto>> GetBookingsByVenueAsync(int venueId)
    {
        IEnumerable<Booking> bookings = await _bookingRepository.GetBookingsByVenueIdAsync(venueId);
        return BookingGuestVisibility.Apply(_mapper.ToDtoWithGroupList(bookings), _currentUser);
    }

    public virtual async Task UpdateBookingAsync(int id, BookingDto bookingDto)
    {
        _ = id; // Required by REST convention (PUT /bookings/{id}) but entity ID comes from DTO
        Booking booking = _mapper.ToEntity(bookingDto);
        Venue? venue = await _venueRepository.GetByIdAsync(booking.VenueId);

        if (bookingDto.PartySize > 0 && booking.ResourceId.HasValue)
        {
            Resource? resource = await _resourceRepository.GetByIdAsync(booking.ResourceId.Value);
            RejectIfResourceCannotFit(resource, venue, bookingDto.PartySize);
        }

        if (!booking.EndTime.HasValue || booking.EndTime.Value < booking.Date)
        {
            booking.EndTime = booking.Date.AddMinutes(venue?.DefaultBookingDurationMinutes ?? 60);
        }

        await _bookingRepository.UpdateAsync(booking);
    }

    public virtual async Task DeleteBookingAsync(int id)
    {
        await _bookingRepository.DeleteAsync(id);
    }

    public virtual async Task<string?> GetVenueNameAsync(int venueId)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId);
        return venue?.Name;
    }

    public virtual async Task<bool> CancelBookingAsync(string bookingRef, string email)
    {
        Booking? booking = await _bookingRepository.GetByRefAsync(bookingRef);
        if (booking == null)
        {
            return false;
        }

        // Both misses return the same false, and neither is logged. A guessed ref is the whole
        // attack against this endpoint, so writing the stored address beside the supplied one put
        // a victim's email into stdout on every failed guess — the same PII the audit trail is
        // careful never to record. The 404 is the only thing a caller learns either way.
        if (!string.Equals(booking.CustomerEmail?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (booking.IsCancelled)
        {
            return true;
        }

        if (!booking.CanBeCancelledAt(DateTime.UtcNow))
        {
            throw new ConflictException("Cannot cancel a booking that has already passed.") { Code = ErrorCodes.BookingAlreadyPast };
        }

        booking.IsCancelled = true;
        booking.CancelledAt = DateTime.UtcNow;
        await _bookingRepository.UpdateAsync(booking);

        if (_notificationQueue != null)
        {
            Venue? venue = await _venueRepository.GetByIdAsync(booking.VenueId);
            _notificationQueue.EnqueueBookingCancelled(booking, venue?.Name ?? "");
        }

        return true;
    }
}
