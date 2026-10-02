using System.Diagnostics.CodeAnalysis;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Outcome status of <see cref="AdminService.SendBookingEmailAsync"/>.
/// </summary>
public enum SendBookingEmailStatus { Sent, NotFound, MissingFields, NoCustomerEmail }

/// <summary>
/// Result of <see cref="AdminService.SendBookingEmailAsync"/>. <see cref="Recipient"/> is populated
/// only on <see cref="Sent"/> so the controller can echo it back in the success message without a
/// second fetch — and left null there for a caller that may not see guest identities, which is
/// what <see cref="SentToHiddenRecipient"/> names. SMTP/transport failures are NOT surfaced here —
/// they propagate as exceptions for the controller to map to a 400, preserving the prior behaviour.
/// </summary>
public record SendBookingEmailResult(SendBookingEmailStatus Status, string? Recipient = null)
{
    public static SendBookingEmailResult Sent(string recipient) => new(SendBookingEmailStatus.Sent, recipient);
    public static SendBookingEmailResult SentToHiddenRecipient() => new(SendBookingEmailStatus.Sent);
    public static SendBookingEmailResult NotFound() => new(SendBookingEmailStatus.NotFound);
    public static SendBookingEmailResult MissingFields() => new(SendBookingEmailStatus.MissingFields);
    public static SendBookingEmailResult NoCustomerEmail() => new(SendBookingEmailStatus.NoCustomerEmail);
}

public class AdminService(
    IBookingRepository bookingRepository,
    IBookingFilterRepository bookingFilterRepository,
    IVenueRepository venueRepository,
    ISectionRepository sectionRepository,
    IResourceRepository resourceRepository,
    IHoldService holdService,
    IEmailService emailService,
    BrandService? brandService = null,
    INotificationQueue? notificationQueue = null,
    IAuditScope? audit = null,
    ICurrentUserService? currentUser = null)
{
    /// <summary>
    /// Castle's generated proxy constructors drop default values, so a Moq class mock reaches only
    /// a constructor of exactly matching arity. This is that constructor.
    /// </summary>
    public AdminService(
        IBookingRepository bookings,
        IBookingFilterRepository bookingFilters,
        IVenueRepository venues,
        ISectionRepository sections,
        IResourceRepository resources,
        IHoldService holds,
        IEmailService email,
        BrandService? brand,
        INotificationQueue? notifications)
        : this(bookings, bookingFilters, venues, sections, resources, holds, email, brand,
            notifications, null, null)
    { }

    private readonly IAuditScope _audit = audit ?? NullAuditScope.Instance;
    private readonly ICurrentUserService _currentUser = currentUser ?? NullCurrentUserService.Instance;
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IBookingFilterRepository _bookingFilterRepository = bookingFilterRepository;
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly ISectionRepository _sectionRepository = sectionRepository;
    private readonly IResourceRepository _resourceRepository = resourceRepository;
    private readonly IHoldService _holdService = holdService;
    private readonly IEmailService _emailService = emailService;
    private readonly BrandService? _brandService = brandService;
    private readonly INotificationQueue? _notificationQueue = notificationQueue;

    public virtual async Task<AdminOverviewDto> GetOverviewAsync()
    {
        DateTime nowUtc = DateTime.UtcNow;
        List<Venue> venues = await _venueRepository.GetAllActiveAsync();

        int totalVenues = venues.Count;
        int totalBookings = await _bookingRepository.CountActiveAsync();
        int totalCapacity = await _bookingRepository.SumActivePartySizeAsync();

        int todayBookingsCount = 0;
        int pausedVenuesCount = 0;
        int todayNoShowsCount = 0;
        int scheduleConflictsCount = 0;
        List<int> scheduleConflictLocationIds = [];
        List<BookingDetailDto> todayBookingsList = [];
        List<LocationPacingDto> todayPacing = [];
        foreach (Venue? r in venues)
        {
            (DateTime start, DateTime end) = TimeZoneHelper.GetUtcRangeForLocalDay(nowUtc, r.Timezone);
            List<Booking> rTodayBookings = await _bookingRepository.GetForVenueInUtcRangeAsync(r.Id, start, end);
            todayBookingsCount += rTodayBookings.Count;
            todayNoShowsCount += rTodayBookings.Count(b => b.Status == BookingStatus.NoShow);
            todayBookingsList.AddRange(rTodayBookings.Select(ToDetailDto));
            if (r.MaxGuestsPerSlot is int cap)
            {
                todayPacing.Add(PacingFor(r, cap, rTodayBookings));
            }

            List<Booking> upcoming = await _bookingRepository.GetFutureForVenueAsync(r.Id, nowUtc);
            int rScheduleConflicts = ScheduleConflictHelper.Conflicting(r, upcoming).Count;
            if (rScheduleConflicts > 0)
            {
                scheduleConflictsCount += rScheduleConflicts;
                scheduleConflictLocationIds.Add(r.Id);
            }

            if (r.BookingsPausedUntil.HasValue && r.BookingsPausedUntil.Value > nowUtc)
            {
                pausedVenuesCount++;
            }
        }

        (List<int> rawCounts, List<string> occupancyDates) = await CountBookingsPerDayAsync(nowUtc, OccupancyChartDays);
        List<int> occupancyData = AsPercentagesOfPeak(rawCounts);

        return new AdminOverviewDto
        {
            TotalVenues = totalVenues,
            TotalBookings = totalBookings,
            TodayBookings = todayBookingsCount,
            TotalCapacity = totalCapacity,
            ActiveHoldsCount = _holdService.GetActiveHoldsCount(),
            PausedVenuesCount = pausedVenuesCount,
            TodayNoShowsCount = todayNoShowsCount,
            ScheduleConflictsCount = scheduleConflictsCount,
            ScheduleConflictLocationIds = scheduleConflictLocationIds,
            OccupancyData = occupancyData,
            OccupancyDates = occupancyDates,
            OccupancyCounts = rawCounts,
            TodayBookingsList = [.. todayBookingsList.OrderBy(b => b.Date)],
            TodayPacing = todayPacing,
        };
    }

    /// <seealso>AdminServiceTests.GetOverviewAsync_ReportsTodaysGuestsPerSlot_ForACappedLocation</seealso>
    private static LocationPacingDto PacingFor(Venue venue, int cap, IEnumerable<Booking> todayBookings) => new()
    {
        VenueId = venue.Id,
        VenueName = venue.Name,
        MaxGuestsPerSlot = cap,
        Slots = GuestPacing.GuestsBySlot(venue, todayBookings)
            .Select(slot => new SlotGuestsDto
            {
                Time = TimeZoneHelper.ConvertUtcToLocal(slot.SlotStartUtc, venue.Timezone)
                    .ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                Guests = slot.Guests,
            })
            .ToList(),
    };

    private const int OccupancyChartDays = 7;

    /// <summary>
    /// Booking counts for the <paramref name="days"/> ending on <paramref name="nowUtc"/>, oldest
    /// first, alongside each day's ISO calendar date — the client toggles its bar labels between
    /// those and relative T-x ones, so both have to come back from the same walk.
    /// </summary>
    private async Task<(List<int> Counts, List<string> Dates)> CountBookingsPerDayAsync(DateTime nowUtc, int days)
    {
        List<int> counts = [];
        List<string> dates = [];

        for (int daysAgo = days - 1; daysAgo >= 0; daysAgo--)
        {
            DateTime dayStart = DateTime.SpecifyKind(nowUtc.Date.AddDays(-daysAgo), DateTimeKind.Utc);
            counts.Add(await _bookingRepository.CountActiveByDayAsync(dayStart, dayStart.AddDays(1)));
            dates.Add(dayStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }

        return (counts, dates);
    }

    /// <summary>
    /// Scales relative to the busiest day rather than to an absolute ceiling, so the peak day
    /// always fills the chart and quiet weeks still read as a shape.
    /// </summary>
    private static List<int> AsPercentagesOfPeak(List<int> counts)
    {
        int peak = MaxOrZero(counts);
        return counts
            .Select(count => peak > 0 ? (int)Math.Round((double)count / peak * 100) : 0)
            .ToList();
    }

    // CountBookingsPerDayAsync always appends OccupancyChartDays entries, so the empty-list guard
    // is unreachable from this call site. Isolated into its own method so excluding it doesn't
    // hide coverage on the branches around it.
    [ExcludeFromCodeCoverage(Justification = "Unreachable: the 7-iteration loop above always populates rawCounts, so Count is never 0 at this call site.")]
    private static int MaxOrZero(List<int> counts) => counts.Count > 0 ? counts.Max() : 0;

    public virtual async Task<List<BookingDetailDto>> GetBookingsAsync(int? venueId, DateTime? bookingDate, string status, string? email = null, string? bookingRef = null, string? query = null)
    {
        List<Booking> bookings = await _bookingFilterRepository.QueryAsync(new BookingFilter
        {
            VenueId = venueId,
            BookingDate = bookingDate,
            Status = status,
            Email = email,
            BookingRef = bookingRef,
            Query = query,
        });

        return await WithNoShowHistoryAsync(bookings);
    }

    public virtual async Task<BookingDetailDto?> GetBookingAsync(int id)
    {
        Booking? b = await _bookingRepository.GetByIdAsync(id);
        return b == null ? null : (await WithNoShowHistoryAsync([b]))[0];
    }

    /// <summary>
    /// Moves a booking along the floor (arrived, in use, finished, no-show), or takes back the
    /// last move while <see cref="Booking.StatusUndoWindow"/> is open. Finishing and no-showing
    /// shorten the slot to now, which is what frees the resource for availability and the waitlist.
    /// </summary>
    /// <seealso>AdminServiceTests.SetBookingStatusAsync_RefusesANoShow_BeforeTheSlotStarts</seealso>
    /// <seealso>AdminServiceTests.SetBookingStatusAsync_Undo_RestoresTheOriginalEndTime</seealso>
    /// <seealso>AdminServiceTests.SetBookingStatusAsync_Refuses_ACancelledBooking</seealso>
    public virtual async Task<BookingDetailDto?> SetBookingStatusAsync(int id, string status)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(id);
        if (booking == null)
        {
            return null;
        }

        BookingStatus target = ParseBookingStatus(status);
        BookingStatus from = booking.Status;
        DateTime? endBefore = booking.EndTime;
        DateTime nowUtc = DateTime.UtcNow;

        if (booking.IsCancelled)
        {
            throw new BusinessRuleException("A cancelled booking takes no status changes.") { Code = ErrorCodes.BookingStatusCancelled };
        }

        if (target == from)
        {
            return (await WithNoShowHistoryAsync([booking]))[0];
        }

        if (booking.UndoStatus(nowUtc) == target)
        {
            booking.UndoStatusChange();
        }
        else if (booking.NextStatuses(nowUtc).Contains(target))
        {
            booking.Advance(target, nowUtc, booking.Venue?.DefaultBookingDurationMinutes ?? BookingDuration.FallbackMinutes);
        }
        else if (target == BookingStatus.NoShow && from == BookingStatus.Booked)
        {
            throw new BusinessRuleException("A booking can only be marked as a no-show once its slot has started.") { Code = ErrorCodes.BookingNoShowBeforeStart };
        }
        else
        {
            throw new BusinessRuleException($"A booking that is {from} cannot be marked {target}.")
            {
                Code = ErrorCodes.BookingStatusTransitionInvalid,
                Args = new Dictionary<string, object> { ["from"] = from.ToString(), ["to"] = target.ToString() },
            };
        }

        await _bookingRepository.UpdateAsync(booking);

        _audit.RecordChange("status", from.ToString(), booking.Status.ToString());
        _audit.RecordChange("endTime", endBefore, booking.EndTime);
        DescribeBooking(AuditActions.BookingStatus, booking,
            $"Marked booking {booking.BookingRef} as {booking.Status}");
        return (await WithNoShowHistoryAsync([booking]))[0];
    }

    private static BookingStatus ParseBookingStatus(string value)
    {
        if (!Enum.TryParse(value, ignoreCase: true, out BookingStatus parsed)
            || !Enum.IsDefined(parsed)
            || char.IsDigit(value.Trim().FirstOrDefault()))
        {
            throw new ValidationException(
                $"Status must be one of: {string.Join(", ", Enum.GetNames<BookingStatus>())}.")
            {
                Code = ErrorCodes.BookingStatusInvalid,
                Args = new Dictionary<string, object> { ["allowed"] = string.Join(", ", Enum.GetNames<BookingStatus>()) },
            };
        }

        return parsed;
    }

    /// <summary>
    /// Maps bookings with each guest's no-show count from every other booking under their email,
    /// counted in one query. Derived rather than stored, so the GDPR purge that removes a
    /// guest's bookings removes their history with it. Left off for a caller that cannot see
    /// who the guest is, since the count is guest history keyed on their email.
    /// </summary>
    /// <seealso>AdminServiceTests.GetBookingAsync_CountsTheGuestsOtherNoShows_CaseInsensitively</seealso>
    /// <seealso>AdminServiceTests.GetBookingAsync_OmitsNoShowHistory_ForAKeyWithoutGuestsScope</seealso>
    private async Task<List<BookingDetailDto>> WithNoShowHistoryAsync(List<Booking> bookings)
    {
        if (BookingGuestVisibility.IsRedactedFor(_currentUser))
        {
            return bookings.Select(ToDetailDto).ToList();
        }

        Dictionary<string, int> noShows = await _bookingRepository.CountNoShowsByEmailAsync(
            bookings.Where(b => !string.IsNullOrWhiteSpace(b.CustomerEmail)).Select(b => b.CustomerEmail!));

        return bookings.Select(b =>
        {
            BookingDetailDto dto = ToDetailDto(b);
            if (!string.IsNullOrWhiteSpace(b.CustomerEmail))
            {
                noShows.TryGetValue(b.CustomerEmail.Trim().ToLowerInvariant(), out int count);
                dto.PreviousNoShows = b.Status == BookingStatus.NoShow ? count - 1 : count;
            }
            return dto;
        }).ToList();
    }

    public virtual Task<BookingDetailDto> CreateBookingAsync(AdminCreateBookingRequest req)
        => BookingWriteGate.RunAsync(() => CreateBookingCoreAsync(req));

    private async Task<BookingDetailDto> CreateBookingCoreAsync(AdminCreateBookingRequest req)
    {
        Resource resource = await _resourceRepository.GetWithSectionVenueAsync(req.ResourceId, req.SectionId)
            ?? throw new ValidationException("Resource not found in the specified section.") { Code = ErrorCodes.BookingResourceNotInSection };

        if (resource.Section!.VenueId != req.VenueId)
        {
            throw new ValidationException("Section does not belong to this venue.") { Code = ErrorCodes.BookingSectionMismatch };
        }

        DateTime newStart = TimeZoneHelper.ConvertLocalToUtc(req.Date, resource.Section.Venue!.Timezone);

        int durationMinutes = BookingDuration.For(resource.Section.Venue, req.PartySize);
        DateTime newEnd = newStart.AddMinutes(durationMinutes);

        bool conflict = await _bookingRepository.HasConflictAsync(req.ResourceId, newStart, newEnd, durationMinutes);

        if (conflict)
        {
            throw new ConflictException("This resource already has a booking that overlaps with the requested time.") { Code = ErrorCodes.BookingResourceConflict };
        }

        if (req.PartySize > resource.Capacity)
        {
            throw new ConflictException($"This resource has capacity {resource.Capacity}, but {req.PartySize} guests were requested.") { Code = ErrorCodes.ResourceCapacityExceeded, Args = new Dictionary<string, object> { ["capacity"] = resource.Capacity, ["requested"] = req.PartySize } };
        }

        var booking = new Booking
        {
            VenueId = req.VenueId,
            SectionId = req.SectionId,
            ResourceId = req.ResourceId,
            Date = newStart,
            EndTime = newStart.AddMinutes(durationMinutes),
            CustomerEmail = req.CustomerEmail,
            CustomerName = req.CustomerName,
            PartySize = req.PartySize,
            BookingRef = BookingRefFactory.GenerateFor(resource.Section.Venue),
        };

        await _bookingRepository.AddAsync(booking);

        Booking? reloaded = await _bookingRepository.GetByIdAsync(booking.Id);
        if (reloaded != null)
        {
            booking = reloaded;
        }

        if (_notificationQueue != null)
        {
            _notificationQueue.EnqueueBookingCreated(booking, booking.Venue!.Name);
            _notificationQueue.EnqueueCapacityCheck(booking.VenueId, booking.Venue!.Name, booking.Date);
        }

        DescribeBooking(AuditActions.BookingCreate, booking,
            $"Created booking {booking.BookingRef} for {booking.PartySize} guests");
        return ToDetailDto(booking);
    }

    public virtual async Task<DateTime?> ExtendBookingAsync(int id, int minutes)
    {
        Booking? booking = await _bookingRepository.FindByIdAsync(id);
        if (booking == null)
        {
            return null;
        }

        Venue? venue = booking.EndTime.HasValue && booking.EndTime.Value > booking.Date
            ? null
            : await _venueRepository.FindByIdAsync(booking.VenueId);
        DateTime from = BookingDuration.ResolveEnd(
            booking.Date, booking.EndTime, venue?.DefaultBookingDurationMinutes);

        DateTime? previousEnd = booking.EndTime;
        booking.EndTime = from.AddMinutes(minutes);
        await _bookingRepository.UpdateAsync(booking);

        _audit.RecordChange("endTime", previousEnd, booking.EndTime);
        DescribeBooking(AuditActions.BookingExtend, booking,
            $"Extended booking {booking.BookingRef} by {minutes} minutes");
        return booking.EndTime;
    }

    public virtual async Task<bool> CancelBookingAsync(int id)
    {
        Booking? booking = await _bookingRepository.FindByIdAsync(id);
        if (booking == null)
        {
            return false;
        }

        if (!booking.IsCancelled && !booking.CanBeCancelledAt(DateTime.UtcNow))
        {
            throw new ConflictException("Cannot cancel a booking that has already passed.") { Code = ErrorCodes.BookingAlreadyPast };
        }

        booking.IsCancelled = true;
        booking.CancelledAt = DateTime.UtcNow;
        await _bookingRepository.UpdateAsync(booking);

        if (_notificationQueue != null)
        {
            Booking? withVenue = await _bookingRepository.GetByIdAsync(id);
            _notificationQueue.EnqueueBookingCancelled(withVenue ?? booking, withVenue?.Venue?.Name ?? "");
        }

        DescribeBooking(AuditActions.BookingCancel, booking,
            $"Cancelled booking {booking.BookingRef}");
        return true;
    }

    public virtual async Task<bool> PurgeBookingAsync(int id)
    {
        Booking? booking = await _bookingRepository.FindByIdAsync(id);
        if (booking == null)
        {
            return false;
        }

        await _bookingRepository.DeleteAsync(id);

        DescribeBooking(AuditActions.BookingPurge, booking,
            $"Permanently deleted booking {booking.BookingRef}");
        return true;
    }

    public virtual async Task<BookingDetailDto?> RestoreBookingAsync(int id)
    {
        Booking? booking = await _bookingRepository.FindByIdAsync(id);
        if (booking == null)
        {
            return null;
        }

        if (!booking.IsCancelled)
        {
            throw new BusinessRuleException("Booking is already active.") { Code = ErrorCodes.BookingAlreadyActive };
        }

        booking.IsCancelled = false;
        booking.CancelledAt = null;
        await _bookingRepository.UpdateAsync(booking);

        DescribeBooking(AuditActions.BookingRestore, booking,
            $"Restored cancelled booking {booking.BookingRef}");
        return ToDetailDto(booking);
    }

    public virtual async Task<BookingDetailDto?> AdminUpdateBookingAsync(int id, AdminUpdateBookingRequest req)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(id);

        if (booking == null)
        {
            return null;
        }

        BookingFields before = BookingFields.From(booking);

        Venue? venue = booking.Venue;
        if (req.VenueId.HasValue && req.VenueId.Value != booking.VenueId)
        {
            Venue? newVenue = await _venueRepository.FindByIdAsync(req.VenueId.Value);
            if (newVenue == null)
            {
                throw new ValidationException("Invalid venue.") { Code = ErrorCodes.VenueNotFound };
            }
            booking.VenueId = req.VenueId.Value;
            venue = newVenue;
        }

        int durationMinutes = venue is null
            ? BookingDuration.FallbackMinutes
            : BookingDuration.For(venue, req.PartySize ?? booking.PartySize);

        if (req.ResourceId.HasValue && req.ResourceId.Value != booking.ResourceId)
        {
            Resource? resource = await _resourceRepository.GetWithSectionForVenueAsync(req.ResourceId.Value, booking.VenueId);

            if (resource == null)
            {
                throw new ValidationException("Invalid resource for this venue.") { Code = ErrorCodes.BookingInvalidResourceForVenue };
            }
            booking.ResourceId = req.ResourceId.Value;
            booking.SectionId = resource.SectionId;
        }
        else if (req.SectionId.HasValue && req.SectionId.Value != booking.SectionId)
        {
            throw new ValidationException("Provide resourceId when reassigning to a different section.") { Code = ErrorCodes.BookingResourceIdRequiredForSectionChange };
        }

        if (req.Date.HasValue && req.Date.Value != booking.Date)
        {
            RescheduleKeepingDuration(booking, req.Date.Value, durationMinutes);
        }

        if (booking.EndTime.HasValue && booking.EndTime.Value < booking.Date)
        {
            booking.EndTime = booking.Date.AddMinutes(durationMinutes);
        }

        await RejectIfMovedOntoATakenUnitAsync(booking, before, id, durationMinutes);

        if (req.PartySize.HasValue)
        {
            int? resolvedResourceId = req.ResourceId ?? booking.ResourceId;
            if (resolvedResourceId.HasValue)
            {
                Resource? currentResource = await _resourceRepository.FindByIdAsync(resolvedResourceId.Value);
                if (currentResource != null && req.PartySize.Value > currentResource.Capacity)
                {
                    throw new BusinessRuleException($"This resource has capacity {currentResource.Capacity}, but {req.PartySize.Value} guests were requested.") { Code = ErrorCodes.ResourceCapacityExceeded, Args = new Dictionary<string, object> { ["capacity"] = currentResource.Capacity, ["requested"] = req.PartySize.Value } };
                }
            }
            booking.PartySize = req.PartySize.Value;
        }
        if (req.CustomerEmail != null)
        {
            booking.CustomerEmail = req.CustomerEmail;
        }
        if (req.CustomerName != null)
        {
            booking.CustomerName = string.IsNullOrWhiteSpace(req.CustomerName) ? null : req.CustomerName.Trim();
        }
        if (req.SpecialRequests != null)
        {
            booking.SpecialRequests = req.SpecialRequests;
        }

        await _bookingRepository.UpdateAsync(booking);

        RecordBookingChanges(before, BookingFields.From(booking));
        DescribeBooking(AuditActions.BookingUpdate, booking, $"Updated booking {booking.BookingRef}");

        // Reloaded through the eager-loading read so the DTO carries the updated names.
        Booking? reloaded = await _bookingRepository.GetByIdAsync(id);
        return reloaded == null ? ToDetailDto(booking) : ToDetailDto(reloaded);
    }

    /// <summary>
    /// The booking fields an admin edit can move, snapshotted either side of the update.
    /// <see cref="Booking.SpecialRequests"/> is deliberately absent: it is guest-authored free text
    /// that routinely names people, and unlike a named field there is nothing masking it on the way
    /// into the entry.
    /// <seealso>AuditTrailTests.NoGuestDetailOnABooking_EverReachesTheTrail</seealso>
    /// </summary>
    private sealed record BookingFields(
        int VenueId,
        int? ResourceId,
        int? SectionId,
        DateTime Date,
        DateTime? EndTime,
        int PartySize,
        string? CustomerEmail,
        string? CustomerName)
    {
        public static BookingFields From(Booking b) => new(
            b.VenueId, b.ResourceId, b.SectionId, b.Date, b.EndTime, b.PartySize,
            b.CustomerEmail, b.CustomerName);
    }

    private void RecordBookingChanges(BookingFields before, BookingFields after)
    {
        _audit.RecordChange("venueId", before.VenueId, after.VenueId);
        _audit.RecordChange("resourceId", before.ResourceId, after.ResourceId);
        _audit.RecordChange("sectionId", before.SectionId, after.SectionId);
        _audit.RecordChange("date", before.Date, after.Date);
        _audit.RecordChange("endTime", before.EndTime, after.EndTime);
        _audit.RecordChange("partySize", before.PartySize, after.PartySize);
        // Recordable only because both sides mask to the redaction marker: the entry says the
        // guest's details were edited without restating them.
        _audit.RecordChange("customerEmail", before.CustomerEmail, after.CustomerEmail);
        _audit.RecordChange("customerName", before.CustomerName, after.CustomerName);
    }

    private void DescribeBooking(string action, Booking booking, string summary)
        => DescribeBooking(action, booking.Id, booking.BookingRef, booking.VenueId, summary);

    private void DescribeBooking(string action, BookingDetailDto booking, string summary)
        => DescribeBooking(action, booking.Id, booking.BookingRef, booking.VenueId, summary);

    /// <summary>
    /// Every booking entry goes through here, which is what keeps them all pointing at a booking
    /// by id and reference rather than by the guest slot at it — the summaries above name the
    /// reference for the same reason. Booking history is deliberately GDPR-purgeable, and an entry
    /// carrying a guest's name, address, note or the body of a mail sent to them would outlive the
    /// purge that was supposed to remove it.
    /// <seealso>AuditTrailTests.BookingEntry_PointsAtTheBookingByItsReference</seealso>
    /// <seealso>AuditTrailTests.NoGuestDetailOnABooking_EverReachesTheTrail</seealso>
    /// </summary>
    private void DescribeBooking(string action, int id, string? bookingRef, int venueId, string summary)
        => _audit.Describe(action, AuditTargets.Booking, AuditTargets.IdOf(id), bookingRef, venueId, summary);

    private void DescribeVenue(string action, Venue venue, string summary)
        => _audit.Describe(action, AuditTargets.Venue, AuditTargets.IdOf(venue.Id),
            venue.Name, venue.Id, summary);

    /// <summary>Moves the slot while keeping its length, so a reschedule never silently resizes it.</summary>
    private static void RescheduleKeepingDuration(Booking booking, DateTime newDate, int fallbackDurationMinutes)
    {
        booking.EndTime = booking.EndTime.HasValue
            ? newDate + (booking.EndTime.Value - booking.Date)
            : newDate.AddMinutes(fallbackDurationMinutes);
        booking.Date = newDate;
    }

    /// <summary>
    /// Rejects an edit that would put this booking on a resource someone else already has. Editing
    /// is how a booking is moved — there is no separate reschedule flow — so this is the only
    /// thing standing between an admin changing a date and the same resource being booked twice.
    /// <para>
    /// Compares against <paramref name="before"/> rather than the request, because the caller has
    /// already written the request onto <paramref name="booking"/>: asking whether the request
    /// differs from the booking is asking whether a value differs from itself, which is what left
    /// this check unreachable for its whole life. Asking whether the booking moved is the question
    /// that survives the caller changing.
    /// </para>
    /// </summary>
    /// <seealso>AdminServiceTests.AdminUpdateBookingAsync_RejectsAMoveOntoAResourceAlreadyBookedThen</seealso>
    /// <seealso>AdminServiceTests.AdminUpdateBookingAsync_AllowsAMoveOntoAFreeSlotOnTheSameResource</seealso>
    private async Task RejectIfMovedOntoATakenUnitAsync(Booking booking, BookingFields before, int id, int durationMinutes)
    {
        bool moved = booking.Date != before.Date || booking.ResourceId != before.ResourceId;
        if (!moved)
        {
            return;
        }

        // The unit the booking actually occupies, not the one the request named: a group booking
        // carries ResourceId = null, and a resource-only check would miss every group on the floor.
        bool taken = await _bookingRepository.IsUnitBookedOnDateAsync(
            booking.ResourceId,
            booking.ResourceGroupId,
            AsUtc(booking.Date),
            OccupancyMinutes(booking, durationMinutes),
            excludeBookingId: id);

        if (taken)
        {
            throw new BusinessRuleException("This update would cause a conflict with an existing booking.") { Code = ErrorCodes.BookingMoveConflict };
        }
    }

    /// <summary>How long the booking holds its unit: its own span when it has one, else the default.</summary>
    private static int OccupancyMinutes(Booking booking, int fallbackMinutes)
    {
        if (!booking.EndTime.HasValue)
        {
            return fallbackMinutes;
        }

        int span = (int)(booking.EndTime.Value - booking.Date).TotalMinutes;
        return span > 0 ? span : fallbackMinutes;
    }

    /// <summary>
    /// A stored <see cref="DateTime"/> as UTC. Everything is persisted UTC, so an Unspecified
    /// kind is already UTC and must be labelled rather than converted —
    /// <see cref="DateTime.ToUniversalTime"/> would read it as server-local and shift it.
    /// </summary>
    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    public virtual async Task<List<LookupDto>> GetVenuesAsync()
    {
        DateTime nowUtc = DateTime.UtcNow;
        return await _venueRepository.GetAllWithActiveBookingsCountAsync(nowUtc);
    }

    public virtual async Task<List<LookupDto>> GetSectionsAsync(int venueId)
    {
        List<Section> sections = await _sectionRepository.GetByVenueAsync(venueId);
        return sections.Select(s => new LookupDto { Id = s.Id, Name = s.Name }).ToList();
    }

    /// <summary>
    /// Persists a new display order for a venue's sections. Accepts the full
    /// ordered list of section IDs (rather than a single swap) so both the up/down
    /// move-button UI and any future bulk-reorder UI can share one endpoint — the
    /// client computes the desired order locally and resends the whole list, matching
    /// the existing "resend full record" convention used by Highlights/SocialLinks.
    /// Returns null when the venue doesn't exist, false when sectionIds doesn't
    /// exactly match the venue's current sections, true on success.
    /// </summary>
    public virtual async Task<bool?> ReorderSectionsAsync(int venueId, List<int> sectionIds)
    {
        bool? reordered = await _sectionRepository.ReorderAsync(venueId, sectionIds);
        if (reordered == true)
        {
            _audit.Describe(AuditActions.VenueReorderSections, AuditTargets.Venue,
                AuditTargets.IdOf(venueId), venueId: venueId,
                summary: $"Reordered {sectionIds.Count} sections");
        }

        return reordered;
    }

    // ── Venues ─────────────────────────────────────────────────────────

    public virtual async Task<bool> PauseVenueBookingsAsync(int venueId, int durationMinutes)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(venueId);
        if (venue == null)
        {
            return false;
        }

        DateTime? previousPausedUntil = venue.BookingsPausedUntil;
        venue.BookingsPausedUntil = DateTime.UtcNow.AddMinutes(durationMinutes);
        await _venueRepository.SaveChangesAsync();

        _audit.RecordChange("bookingsPausedUntil", previousPausedUntil, venue.BookingsPausedUntil);
        DescribeVenue(AuditActions.VenuePause, venue,
            $"Paused bookings at {venue.Name} for {durationMinutes} minutes");
        return true;
    }

    public virtual async Task<bool> UnpauseVenueBookingsAsync(int venueId)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(venueId);
        if (venue == null)
        {
            return false;
        }

        DateTime? previousPausedUntil = venue.BookingsPausedUntil;
        venue.BookingsPausedUntil = null;
        await _venueRepository.SaveChangesAsync();

        _audit.RecordChange("bookingsPausedUntil", previousPausedUntil, null);
        DescribeVenue(AuditActions.VenueUnpause, venue,
            $"Resumed bookings at {venue.Name}");
        return true;
    }

    public virtual async Task<List<BookingDetailDto>?> ExtendAllActiveBookingsAsync(int venueId, int extensionMinutes)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(venueId);
        if (venue == null)
        {
            return null;
        }

        DateTime nowUtc = DateTime.UtcNow;

        List<Booking> activeBookings = await _bookingRepository.GetInProgressForVenueAsync(venueId, nowUtc, venue.DefaultBookingDurationMinutes);

        foreach (Booking? booking in activeBookings)
        {
            DateTime currentEndTime = booking.EndTime ?? booking.Date.AddMinutes(venue.DefaultBookingDurationMinutes);
            booking.EndTime = currentEndTime.AddMinutes(extensionMinutes);
        }

        // Single SaveChanges flushes every mutated EndTime — same DB round-trip count as the
        // original implementation. The entities are already tracked on the shared DI-scoped DbContext.
        await _bookingRepository.SaveChangesAsync();

        DescribeVenue(AuditActions.VenueExtendBookings, venue,
            $"Extended {activeBookings.Count} in-progress bookings at {venue.Name} by {extensionMinutes} minutes");
        return activeBookings.Select(ToDetailDto).ToList();
    }

    public virtual async Task<VenueDto> CreateVenueAsync(string name, string? address)
    {
        var venue = new Venue { Name = name.Trim(), Address = address?.Trim() };
        await _venueRepository.AddAsync(venue);

        DescribeVenue(AuditActions.VenueCreate, venue,
            $"Created the location \"{venue.Name}\"");

        return new VenueDto
        {
            Id = venue.Id,
            Name = venue.Name,
            Address = venue.Address,
            DefaultBookingDurationMinutes = venue.DefaultBookingDurationMinutes,
            BookingRefFormat = venue.BookingRefFormat.ToString(),
            Sections = [],
        };
    }

    public virtual async Task<bool> SetArchivedAsync(int id, bool archived)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null)
        {
            return false;
        }

        _audit.RecordChange("isArchived", venue.IsArchived, archived);
        venue.IsArchived = archived;
        await _venueRepository.SaveChangesAsync();

        DescribeVenue(
            archived ? AuditActions.VenueArchive : AuditActions.VenueRestore,
            venue,
            archived
                ? $"Archived the location \"{venue.Name}\""
                : $"Restored the location \"{venue.Name}\"");
        return true;
    }

    public virtual async Task<VenueDeletePreviewDto?> GetVenueDeletePreviewAsync(int id)
    {
        return await _venueRepository.GetDeletePreviewAsync(id, DateTime.UtcNow);
    }

    public virtual async Task<bool> DeleteVenueAsync(int id)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null)
        {
            return false;
        }

        // Archive-then-purge is a rule, not a UI convention: a live location's bookings are
        // reachable by the guests who made them, and this cascade destroys them irreversibly.
        if (!venue.IsArchived)
        {
            throw new BusinessRuleException(
                "Archive this location before deleting it. Archiving takes it off the public site and can be undone; deleting cannot.")
            { Code = ErrorCodes.VenueArchiveBeforeDelete };
        }

        // Cascade-delete all bookings for this venue (cancelled and active alike), then the venue row,
        // in a single SaveChanges — faithful to the original ".Where(b => b.VenueId == id)" semantics.
        List<Booking> bookings = (await _bookingRepository.GetBookingsByVenueIdAsync(id)).ToList();
        _bookingRepository.RemoveRange(bookings);
        _venueRepository.Remove(venue);
        await _venueRepository.SaveChangesAsync();

        DescribeVenue(AuditActions.VenueDelete, venue,
            $"Permanently deleted the location \"{venue.Name}\" and its {bookings.Count} bookings");
        return true;
    }

    // ── Resources ──────────────────────────────────────────────────────────────

    public virtual async Task<List<SectionDto>?> GetResourcesAsync(int venueId)
    {
        List<Section> sections = await _sectionRepository.GetByVenueAsync(venueId, includeResources: true);

        if (sections.Count == 0)
        {
            return null;
        }

        return sections.Select(s => new SectionDto
        {
            Id = s.Id,
            Name = s.Name,
            SortOrder = s.SortOrder,
            Resources = s.Resources.Select(t => new ResourceDto
            {
                Id = t.Id,
                Name = t.Name,
                Capacity = t.Capacity,
                WalkInOnly = t.WalkInOnly,
            }).ToList(),
        }).ToList();
    }

    /// <summary>
    /// Sends an arbitrary admin-authored email to a booking's customer. Resolves the booking,
    /// validates that subject/body/customer-email are all present, wraps the body in the brand
    /// template (via <see cref="EmailHelper.BuildEmailContentFromBrand"/>), and dispatches via
    /// <see cref="IEmailService.SendEmailAsync"/>. SMTP/transport failures propagate as exceptions
    /// — the controller catches them to map a 400, preserving the prior behaviour.
    /// <para>
    /// Reads the recipient off the entity rather than <see cref="ToDetailDto"/>'s output, which is
    /// guest-redacted: <see cref="ApiKeyScopes.Guests"/> governs what a caller may <em>see</em>,
    /// not what the server may send to, so a key holding <c>bookings:write</c> without it would
    /// otherwise be told a booking with an address on it has none. What that caller must not get
    /// back is the address itself, so the echoed recipient — and only that — is redacted.
    /// </para>
    /// <seealso>AdminControllerEmailTests.SendEmail_WithoutGuestScope_StillReachesTheGuest</seealso>
    /// <seealso>AdminControllerEmailTests.SendEmail_WithoutGuestScope_DoesNotEchoTheAddressBack</seealso>
    /// </summary>
    public virtual async Task<SendBookingEmailResult> SendBookingEmailAsync(int bookingId, SendBookingEmailRequest req)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(bookingId);
        if (booking == null)
        {
            return SendBookingEmailResult.NotFound();
        }

        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Body))
        {
            return SendBookingEmailResult.MissingFields();
        }

        if (string.IsNullOrWhiteSpace(booking.CustomerEmail))
        {
            return SendBookingEmailResult.NoCustomerEmail();
        }

        string htmlBody = await EmailHelper.BuildEmailContentFromBrand(_brandService, req.Body);
        await _emailService.SendEmailAsync(booking.CustomerEmail, req.Subject, htmlBody);

        DescribeBooking(AuditActions.BookingEmail, booking,
            $"Emailed the guest on booking {booking.BookingRef}");
        return BookingGuestVisibility.IsRedactedFor(_currentUser)
            ? SendBookingEmailResult.SentToHiddenRecipient()
            : SendBookingEmailResult.Sent(booking.CustomerEmail);
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

    // Instance rather than static so the single mapping choke point can also apply
    // BookingGuestVisibility — every caller returning a BookingDetailDto routes through here, so
    // that is the one place the redaction rule needs to live.
    private BookingDetailDto ToDetailDto(Booking b)
    {
        DateTime dateUtc = AsUtc(b.Date);
        DateTime? endTimeUtc = b.EndTime.HasValue ? AsUtc(b.EndTime.Value) : null;
        DateTime? cancelledAtUtc = b.CancelledAt.HasValue ? AsUtc(b.CancelledAt.Value) : null;

        // Group booking: Resource/ResourceId are null (the booking reserves a combinable group). Show a
        // readable group label + the group id so the admin grid doesn't render the row as "Resource".
        string? resourceName = b.Resource?.Name ?? (b.ResourceId.HasValue ? $"Resource {b.ResourceId}" : null);
        int? resourceId = b.ResourceId;
        if (resourceName is null && b.ResourceGroup is not null)
        {
            resourceName = BookingMapper.GroupLabel(b.ResourceGroup);
            resourceId = null;
        }
        resourceName ??= "Resource";

        var dto = new BookingDetailDto
        {
            Id = b.Id,
            VenueId = b.VenueId,
            VenueName = b.Venue?.Name,
            Timezone = b.Venue?.Timezone,
            SectionId = b.SectionId,
            SectionName = b.Section?.Name ?? (b.SectionId.HasValue ? $"Section {b.SectionId}" : "Section"),
            ResourceId = resourceId,
            ResourceGroupId = b.ResourceGroupId,
            ResourceName = resourceName,
            Date = dateUtc,
            EndTime = BookingDuration.ResolveEnd(
                dateUtc, endTimeUtc, b.Venue?.DefaultBookingDurationMinutes),
            CustomerEmail = b.CustomerEmail,
            CustomerName = b.CustomerName,
            PartySize = b.PartySize,
            SpecialRequests = b.SpecialRequests,
            BookingRef = b.BookingRef,
            IsCancelled = b.IsCancelled,
            CancelledAt = cancelledAtUtc,
            Status = b.Status.ToString(),
            NextStatuses = b.NextStatuses(DateTime.UtcNow).Select(s => s.ToString()).ToList(),
            UndoStatus = b.UndoStatus(DateTime.UtcNow)?.ToString(),
        };
        return BookingGuestVisibility.Apply(dto, _currentUser);
    }
}
