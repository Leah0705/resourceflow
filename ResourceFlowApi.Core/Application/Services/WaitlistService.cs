using System.Security.Cryptography;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// The walk-in queue: guests join at the front desk or from the public site, staff call and assign them,
/// and assigning turns the entry into an ordinary <see cref="Booking"/> so the floor, availability
/// and reporting all see the resource as taken.
/// </summary>
public class WaitlistService(
    IWaitlistRepository waitlistRepository,
    IVenueRepository venueRepository,
    IBookingRepository bookingRepository,
    ResourceAutoAssigner autoAssigner,
    ISystemClock clock,
    IWaitlistReadyNotifier? readyNotifier = null,
    INotificationQueue? notificationQueue = null,
    ICurrentUserService? currentUser = null,
    IAuditScope? audit = null)
{
    /// <summary>A party still queued this long after joining has almost certainly gone, and would clog the queue.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    /// <summary>Entries hold a guest's name and email, so they are deleted this long after joining.</summary>
    public static readonly TimeSpan RetainFor = TimeSpan.FromDays(7);

    private const string RefAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    private readonly IWaitlistRepository _waitlist = waitlistRepository;
    private readonly IVenueRepository _venues = venueRepository;
    private readonly IBookingRepository _bookings = bookingRepository;
    private readonly ResourceAutoAssigner _autoAssigner = autoAssigner;
    private readonly ISystemClock _clock = clock;
    private readonly IWaitlistReadyNotifier? _readyNotifier = readyNotifier;
    private readonly INotificationQueue? _notificationQueue = notificationQueue;
    private readonly ICurrentUserService _currentUser = currentUser ?? NullCurrentUserService.Instance;
    private readonly IAuditScope _audit = audit ?? NullAuditScope.Instance;

    /// <summary>
    /// True while the public site offers the queue: the location is walk-in only and open at
    /// <paramref name="nowUtc"/>. Staff can add a party at any time.
    /// </summary>
    /// <seealso>WaitlistServiceTests.JoinAsync_Rejects_OnADayThatTakesBookings</seealso>
    /// <seealso>WaitlistServiceTests.JoinAsync_Rejects_OutsideOpeningHours</seealso>
    /// <seealso>WaitlistServiceTests.JoinAsync_Accepts_DuringAWalkInSlot</seealso>
    public static bool AcceptsGuestsAt(Venue venue, DateTime nowUtc)
        => venue.IsWalkInOnlyAt(nowUtc) && venue.IsOpenAt(nowUtc);

    public async Task<WaitlistStatusDto> JoinAsync(int venueId, JoinWaitlistRequest req)
    {
        Venue venue = await LoadVenueAsync(venueId);
        DateTime now = _clock.UtcNow;

        if (!venue.IsWalkInOnlyAt(now))
        {
            throw new ConflictException("This location isn't running a walk-in waitlist today.") { Code = ErrorCodes.WaitlistNotWalkInNow };
        }

        if (!venue.IsOpenAt(now))
        {
            throw new ConflictException("This location is closed right now.") { Code = ErrorCodes.WaitlistClosedNow };
        }

        WaitlistEntry entry = await AddEntryAsync(venue, req, now);
        return await BuildStatusAsync(entry, venue);
    }

    public async Task<WaitlistEntryDto> AddByStaffAsync(int venueId, JoinWaitlistRequest req)
    {
        Venue venue = await LoadVenueAsync(venueId);
        WaitlistEntry entry = await AddEntryAsync(venue, req, _clock.UtcNow);

        Describe(AuditActions.WaitlistAdd, entry, $"Added ticket #{entry.Number} for {entry.PartySize} guests to the waitlist");
        WaitlistBoardDto board = await GetBoardAsync(venueId);
        return board.Entries.First(e => e.Id == entry.Id);
    }

    /// <summary>The wait a party of <paramref name="partySize"/> would face joining behind today's queue.</summary>
    /// <seealso>WaitlistServiceTests.GetQuoteAsync_QuotesANewPartyBehindTheQueue</seealso>
    public async Task<WaitlistQuoteDto> GetQuoteAsync(int venueId, int partySize)
    {
        Venue venue = await LoadVenueAsync(venueId);
        DateTime now = _clock.UtcNow;
        List<WaitlistEntry> queue = Order(await _waitlist.GetActiveForVenueAsync(venueId));
        List<int> parties = queue.Select(e => e.PartySize).Append(partySize).ToList();
        IReadOnlyList<WaitEstimator.Estimate?> estimates = await EstimateAsync(venue, parties, now);

        return new WaitlistQuoteDto
        {
            VenueId = venueId,
            AcceptingGuests = AcceptsGuestsAt(venue, now),
            PartiesWaiting = queue.Count,
            EstimatedWaitMinutes = WaitEstimator.MinutesUntil(estimates[^1]?.StartAt, now),
        };
    }

    public async Task<WaitlistStatusDto?> GetStatusAsync(string entryRef)
    {
        WaitlistEntry? entry = await _waitlist.GetByRefAsync(entryRef);
        if (entry == null)
        {
            return null;
        }

        Venue? venue = await _venues.GetByIdAsync(entry.VenueId);
        return venue == null ? null : await BuildStatusAsync(entry, venue);
    }

    /// <summary>The guest withdrawing. Idempotent once the entry has left the queue.</summary>
    public async Task<bool> LeaveAsync(string entryRef)
    {
        WaitlistEntry? entry = await _waitlist.GetByRefAsync(entryRef);
        if (entry == null)
        {
            return false;
        }

        if (entry.IsActive)
        {
            Close(entry, WaitlistStatus.Left);
            await _waitlist.SaveChangesAsync();
        }
        return true;
    }

    /// <summary>
    /// Points the "resource ready" push at this device, replacing whichever device asked before.
    /// The ref is the guest's whole identity here, as on leave.
    /// </summary>
    /// <seealso>WaitlistServiceTests.SetPushAsync_StoresTheAddress_AndReplacesAnEarlierDevice</seealso>
    /// <seealso>WaitlistServiceTests.SetPushAsync_RejectsAnInvalidAddress</seealso>
    /// <seealso>WaitlistServiceTests.SetPushAsync_RejectsAnEntryThatHasLeft</seealso>
    public async Task<bool> SetPushAsync(string entryRef, WaitlistPushRequest req)
    {
        WaitlistEntry? entry = await _waitlist.GetByRefAsync(entryRef);
        if (entry == null)
        {
            return false;
        }

        if (!entry.IsActive)
        {
            throw new ConflictException("This party has already left the waitlist.") { Code = ErrorCodes.WaitlistNotActive };
        }

        string endpoint = req.Endpoint.Trim();
        bool hasKeys = req.Channel != GuestPushChannels.WebPush
            || (!string.IsNullOrWhiteSpace(req.P256dh) && !string.IsNullOrWhiteSpace(req.Auth));
        if (!hasKeys || !PushEndpointValidator.IsValidFor(req.Channel, endpoint))
        {
            throw new ValidationException("The push address is not one this server will send to.") { Code = ErrorCodes.WaitlistPushInvalid };
        }

        entry.PushChannel = req.Channel;
        entry.PushEndpoint = endpoint;
        entry.PushP256dh = req.P256dh;
        entry.PushAuth = req.Auth;
        await _waitlist.SaveChangesAsync();
        return true;
    }

    public async Task<WaitlistBoardDto> GetBoardAsync(int venueId)
    {
        Venue venue = await LoadVenueAsync(venueId);
        DateTime now = _clock.UtcNow;
        List<WaitlistEntry> queue = Order(await _waitlist.GetActiveForVenueAsync(venueId));
        IReadOnlyList<WaitEstimator.Estimate?> estimates = await EstimateAsync(venue, queue, now);

        var canFitBySize = new Dictionary<int, bool>();
        foreach (int partySize in queue.Select(e => e.PartySize).Distinct())
        {
            canFitBySize[partySize] = (await _autoAssigner.BuildCandidatesAsync(venue, partySize, now, includeWalkInOnly: true)).Count > 0;
        }

        var entries = new List<WaitlistEntryDto>(queue.Count);
        for (int i = 0; i < queue.Count; i++)
        {
            WaitlistEntry e = queue[i];
            bool canAssignNow = canFitBySize[e.PartySize];
            entries.Add(new WaitlistEntryDto
            {
                Id = e.Id,
                Number = e.Number,
                Name = e.Name,
                Email = e.Email,
                PartySize = e.PartySize,
                Status = StatusName(e.Status),
                JoinedAt = e.CreatedAt,
                NotifiedAt = e.NotifiedAt,
                PartiesAhead = i,
                EstimatedWaitMinutes = WaitEstimator.MinutesUntil(estimates[i]?.StartAt, now),
                CanAssignNow = canAssignNow,
                SkipsNumber = canAssignNow && estimates[i]?.FreeResourceHeldBy is { } ahead ? queue[ahead].Number : null,
            });
        }

        return new WaitlistBoardDto
        {
            VenueId = venueId,
            AcceptingGuests = AcceptsGuestsAt(venue, now),
            Entries = BookingGuestVisibility.Apply(entries, _currentUser),
        };
    }

    /// <summary>Calls the party up. Calling again re-sends the message.</summary>
    public async Task NotifyAsync(int entryId)
    {
        WaitlistEntry entry = await LoadActiveEntryAsync(entryId);
        entry.Status = WaitlistStatus.Notified;
        entry.NotifiedAt = _clock.UtcNow;
        await _waitlist.SaveChangesAsync();

        if (_readyNotifier != null)
        {
            await _readyNotifier.NotifyAsync(entry, entry.Venue);
            // Saves the push address the notifier cleared, if the device no longer exists.
            await _waitlist.SaveChangesAsync();
        }

        Describe(AuditActions.WaitlistNotify, entry, $"Called ticket #{entry.Number}");
    }

    /// <summary>
    /// Assigns the party to a unit free for a whole slot from now, recording it as a booking.
    /// </summary>
    /// <seealso>WaitlistServiceTests.AssignAsync_CreatesABookingOnTheSmallestFreeResource</seealso>
    /// <seealso>WaitlistServiceTests.AssignAsync_RecordsTheBookingAsInUse</seealso>
    /// <seealso>WaitlistServiceTests.AssignAsync_UsesAWalkInOnlyResource</seealso>
    /// <seealso>WaitlistServiceTests.AssignAsync_Rejects_WhenNoResourceIsFree</seealso>
    /// <seealso>WaitlistServiceTests.AssignAsync_Rejects_AResourceThatIsNotFree</seealso>
    public Task<AssignWaitlistEntryResponse> AssignAsync(int entryId, AssignWaitlistEntryRequest req)
        => BookingWriteGate.RunAsync(() => AssignCoreAsync(entryId, req));

    private async Task<AssignWaitlistEntryResponse> AssignCoreAsync(int entryId, AssignWaitlistEntryRequest req)
    {
        WaitlistEntry entry = await LoadActiveEntryAsync(entryId);
        Venue venue = await LoadVenueAsync(entry.VenueId);
        DateTime now = _clock.UtcNow;

        IReadOnlyList<ResourceCandidate> free = await _autoAssigner.BuildCandidatesAsync(venue, entry.PartySize, now, includeWalkInOnly: true);
        ResourceCandidate unit = PickUnit(free, req)
            ?? throw new ConflictException("No free resource can fit this party right now.") { Code = ErrorCodes.WaitlistNoResourceFree };

        var booking = new Booking
        {
            VenueId = venue.Id,
            SectionId = unit.SectionId,
            ResourceId = unit.IsGroup ? null : unit.ResourceId,
            ResourceGroupId = unit.ResourceGroupId,
            Date = now,
            EndTime = now.AddMinutes(BookingDuration.For(venue, entry.PartySize)),
            CustomerName = entry.Name,
            CustomerEmail = entry.Email,
            PartySize = entry.PartySize,
            BookingRef = BookingRefFactory.GenerateFor(venue),
            Status = BookingStatus.InUse,
        };
        await _bookings.AddAsync(booking);

        Close(entry, WaitlistStatus.InUse);
        entry.BookingId = booking.Id;
        await _waitlist.SaveChangesAsync();

        _notificationQueue?.EnqueueBookingCreated(booking, venue.Name);
        Describe(AuditActions.WaitlistAssign, entry, $"Assigned ticket #{entry.Number} to booking {booking.BookingRef}");

        return new AssignWaitlistEntryResponse
        {
            Entry = BookingGuestVisibility.Apply(ToClosedDto(entry), _currentUser),
            BookingId = booking.Id,
            BookingRef = booking.BookingRef,
        };
    }

    public async Task RemoveAsync(int entryId)
    {
        WaitlistEntry entry = await LoadActiveEntryAsync(entryId);
        Close(entry, WaitlistStatus.Left);
        await _waitlist.SaveChangesAsync();

        Describe(AuditActions.WaitlistRemove, entry, $"Removed ticket #{entry.Number} from the waitlist");
    }

    /// <summary>Expires parties queued past <see cref="StaleAfter"/> and hard-deletes entries past <see cref="RetainFor"/>.</summary>
    /// <seealso>WaitlistServiceTests.SweepAsync_ExpiresEntriesPastStaleAfter_AndDeletesPastRetainFor</seealso>
    public virtual async Task SweepAsync()
    {
        DateTime now = _clock.UtcNow;
        await _waitlist.ExpireActiveCreatedBeforeAsync(now - StaleAfter, now);
        await _waitlist.DeleteCreatedBeforeAsync(now - RetainFor);
    }

    private async Task<WaitlistEntry> AddEntryAsync(Venue venue, JoinWaitlistRequest req, DateTime now)
    {
        string name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ValidationException("A name is required to join the waitlist.") { Code = ErrorCodes.WaitlistNameRequired };
        }

        string? email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
        if (email != null && !EmailValidator.IsValid(email))
        {
            throw new ValidationException("That email address doesn't look right.") { Code = ErrorCodes.WaitlistEmailInvalid };
        }

        if (WaitEstimator.EstimateStartTimes(venue, new[] { req.PartySize }, new Dictionary<int, DateTime>(), now)[0] is null)
        {
            throw new ConflictException($"No resource here can fit a party of {req.PartySize}.")
            { Code = ErrorCodes.WaitlistPartyTooLarge, Args = new Dictionary<string, object> { ["partySize"] = req.PartySize } };
        }

        (DateTime dayStart, _) = TimeZoneHelper.GetUtcRangeForLocalDay(now, venue.Timezone);
        var entry = new WaitlistEntry
        {
            VenueId = venue.Id,
            Venue = venue,
            Ref = RandomNumberGenerator.GetString(RefAlphabet, WaitlistFields.RefLength),
            Number = await _waitlist.CountCreatedSinceAsync(venue.Id, dayStart) + 1,
            Name = name,
            PartySize = req.PartySize,
            Email = email,
            Locale = SupportedLocales.IsSupported(req.Locale) ? req.Locale! : "en",
            Status = WaitlistStatus.Waiting,
            CreatedAt = now,
        };
        return await _waitlist.AddAsync(entry);
    }

    private async Task<WaitlistStatusDto> BuildStatusAsync(WaitlistEntry entry, Venue venue)
    {
        var dto = new WaitlistStatusDto
        {
            Ref = entry.Ref,
            Number = entry.Number,
            VenueId = venue.Id,
            VenueName = venue.Name,
            Name = entry.Name,
            PartySize = entry.PartySize,
            Status = StatusName(entry.Status),
            JoinedAt = entry.CreatedAt,
            NotifiedAt = entry.NotifiedAt,
            PushEnabled = entry.PushEndpoint != null,
        };

        if (!entry.IsActive)
        {
            return dto;
        }

        DateTime now = _clock.UtcNow;
        List<WaitlistEntry> queue = Order(await _waitlist.GetActiveForVenueAsync(venue.Id));
        int index = queue.FindIndex(e => e.Id == entry.Id);
        IReadOnlyList<WaitEstimator.Estimate?> estimates = await EstimateAsync(venue, queue, now);

        dto.PartiesAhead = index;
        dto.EstimatedWaitMinutes = index < 0 ? null : WaitEstimator.MinutesUntil(estimates[index]?.StartAt, now);
        return dto;
    }

    private Task<IReadOnlyList<WaitEstimator.Estimate?>> EstimateAsync(Venue venue, List<WaitlistEntry> queue, DateTime now)
        => EstimateAsync(venue, queue.Select(e => e.PartySize).ToList(), now);

    private async Task<IReadOnlyList<WaitEstimator.Estimate?>> EstimateAsync(Venue venue, List<int> partySizes, DateTime now)
    {
        List<Booking> inUse = await _bookings.GetInProgressForVenueAsync(venue.Id, now, venue.DefaultBookingDurationMinutes);
        return WaitEstimator.EstimateStartTimes(
            venue,
            partySizes,
            WaitEstimator.ResourceFreeTimes(venue, inUse),
            now);
    }

    /// <summary>
    /// Called parties first, since they are about to take a resource, then everyone else in the
    /// order they joined.
    /// </summary>
    /// <seealso>WaitlistServiceTests.GetBoardAsync_PutsCalledPartiesAheadOfTheQueue</seealso>
    private static List<WaitlistEntry> Order(IEnumerable<WaitlistEntry> active)
        => active
            .OrderBy(e => e.Status == WaitlistStatus.Notified ? 0 : 1)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

    private static ResourceCandidate? PickUnit(IReadOnlyList<ResourceCandidate> free, AssignWaitlistEntryRequest req)
    {
        if (req.ResourceGroupId is { } groupId)
        {
            return free.FirstOrDefault(c => c.IsGroup && c.ResourceGroupId == groupId);
        }

        if (req.ResourceId is { } resourceId)
        {
            return free.FirstOrDefault(c => !c.IsGroup && c.ResourceId == resourceId);
        }

        return free.Count > 0 ? free[0] : null;
    }

    private async Task<Venue> LoadVenueAsync(int venueId)
        => await _venues.GetByIdAsync(venueId)
            ?? throw new NotFoundException("Venue not found.") { Code = ErrorCodes.VenueNotFound };

    private async Task<WaitlistEntry> LoadActiveEntryAsync(int entryId)
    {
        WaitlistEntry entry = await _waitlist.GetByIdAsync(entryId)
            ?? throw new NotFoundException("Waitlist entry not found.") { Code = ErrorCodes.WaitlistNotFound };

        if (!entry.IsActive)
        {
            throw new ConflictException("This party has already left the waitlist.") { Code = ErrorCodes.WaitlistNotActive };
        }
        return entry;
    }

    private void Close(WaitlistEntry entry, WaitlistStatus status)
    {
        entry.Status = status;
        entry.ClosedAt = _clock.UtcNow;
        entry.ClearPush();
    }

    private static WaitlistEntryDto ToClosedDto(WaitlistEntry e) => new()
    {
        Id = e.Id,
        Number = e.Number,
        Name = e.Name,
        Email = e.Email,
        PartySize = e.PartySize,
        Status = StatusName(e.Status),
        JoinedAt = e.CreatedAt,
        NotifiedAt = e.NotifiedAt,
    };

    private static string StatusName(WaitlistStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>
    /// Entries are named by ticket number, never by the guest: the audit trail outlives the
    /// seven-day retention that deletes the name and email.
    /// </summary>
    private void Describe(string action, WaitlistEntry entry, string summary)
        => _audit.Describe(action, AuditTargets.WaitlistEntry, AuditTargets.IdOf(entry.Id),
            $"#{entry.Number}", entry.VenueId, summary);
}
