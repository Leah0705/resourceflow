using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

public class VenueManagementService(
    IVenueRepository venueRepository,
    ISectionRepository sectionRepository,
    IResourceRepository resourceRepository,
    IBookingRepository bookingRepository,
    IResourceGroupRepository resourceGroupRepository,
    IAuditScope? audit = null,
    ICurrentUserService? currentUser = null)
{
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly ISectionRepository _sectionRepository = sectionRepository;
    private readonly IResourceRepository _resourceRepository = resourceRepository;
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IResourceGroupRepository _resourceGroupRepository = resourceGroupRepository;
    private readonly IAuditScope _audit = audit ?? NullAuditScope.Instance;
    private readonly ICurrentUserService _currentUser = currentUser ?? NullCurrentUserService.Instance;

    // Allowed start-time interval values — kept small and sane so the availability
    // slot-generation loop can't be sent into a degenerate (e.g. 0 or negative) spin.
    private static readonly HashSet<int> _allowedBookingSlotIntervalsMinutes = [15, 30, 60];

    /// <summary>
    /// Resolves the wire-format string ("AlphaNumeric"/"Numeric", case-insensitive) to the enum.
    /// Rejects the numeric spellings <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// accepts by default ("1", "7"), so the API contract stays the member names only.
    /// </summary>
    private static BookingRefFormat ParseBookingRefFormat(string value)
    {
        if (!Enum.TryParse(value, ignoreCase: true, out BookingRefFormat parsed)
            || !Enum.IsDefined(parsed)
            || char.IsDigit(value.Trim().FirstOrDefault()))
        {
            throw new ValidationException(
                $"BookingRefFormat must be one of: {string.Join(", ", Enum.GetNames<BookingRefFormat>())}.")
            {
                Code = ErrorCodes.VenueBookingRefFormatInvalid,
                Args = new Dictionary<string, object> { ["allowed"] = string.Join(", ", Enum.GetNames<BookingRefFormat>()) }
            };
        }

        return parsed;
    }

    // ── Venues ─────────────────────────────────────────────────────────

    public async Task<List<VenueDto>> GetAllAsync()
    {
        List<Venue> venues = await _venueRepository.GetAllActiveWithSectionsAsync();
        return venues.Select(ToDto).ToList();
    }

    public async Task<VenueDto?> GetByIdAsync(int id)
    {
        Venue? r = await _venueRepository.GetByIdAsync(id);
        return r == null ? null : ToDto(r);
    }

    public async Task<VenueDto> CreateAsync(VenueDto dto)
    {
        var entity = new Venue
        {
            Name = dto.Name,
            Address = dto.Address,
            OpenTime = string.IsNullOrWhiteSpace(dto.OpenTime) ? OpeningHourDefaults.Open : dto.OpenTime,
            CloseTime = string.IsNullOrWhiteSpace(dto.CloseTime) ? OpeningHourDefaults.Close : dto.CloseTime,
            OpenDays = string.IsNullOrWhiteSpace(dto.OpenDays) ? "1,2,3,4,5,6,7" : dto.OpenDays,
            Timezone = string.IsNullOrWhiteSpace(dto.Timezone) ? "UTC" : dto.Timezone,
            PhoneNumber = dto.PhoneNumber == null ? null : ContactFields.NormalizePhone(dto.PhoneNumber),
            EmailAddress = dto.EmailAddress == null ? null : ContactFields.NormalizeEmail(dto.EmailAddress),
            DefaultBookingDurationMinutes = dto.DefaultBookingDurationMinutes,
            BookingSlotIntervalMinutes = dto.BookingSlotIntervalMinutes,
            MaxSpareCapacity = dto.MaxSpareCapacity,
            MaxGuestsPerSlot = dto.MaxGuestsPerSlot,
            BookingRefFormat = string.IsNullOrWhiteSpace(dto.BookingRefFormat)
                ? BookingRefFormat.AlphaNumeric
                : ParseBookingRefFormat(dto.BookingRefFormat),
            Sections = dto.Sections.Select((s, index) => new Section
            {
                Name = s.Name,
                SortOrder = index,
                Resources = s.Resources.Select(t => new Resource { Name = t.Name, Capacity = t.Capacity, WalkInOnly = t.WalkInOnly }).ToList()
            }).ToList()
        };

        // After the entity is populated, so ApplyOpenHours can collapse identical days back
        // into OpenTime/CloseTime rather than storing redundant JSON.
        if (dto.OpenHours is { Count: > 0 })
        {
            OpeningHoursHelper.ApplyOpenHours(entity, dto.OpenHours);
        }

        DurationRulesHelper.Apply(entity, dto.DurationRules);

        await _venueRepository.AddAsync(entity);

        DescribeVenue(AuditActions.VenueCreate, entity,
            $"Created the location \"{entity.Name}\"");
        return ToDto(entity);
    }

    public async Task<VenueDto?> UpdateAsync(int id, UpdateVenueRequest req)
    {
        Venue? r = await _venueRepository.FindByIdAsync(id);
        if (r == null)
        {
            return null;
        }

        VenueFields before = VenueFields.From(r);

        r.Name = req.Name;
        r.Address = req.Address;
        if (req.Description != null)
        {
            r.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        }
        if (req.GuideUrl != null)
        {
            // Blank clears (matching the brand-text whitespace-to-null convention); a non-blank
            // value must be either an absolute http(s) URL (an externally hosted guide) or the
            // instance-served "/media/guide-<id>.pdf" path written by MediaService.UploadGuideAsync.
            // The served path is relative and intentionally bypasses UrlValidator (which only
            // accepts absolute URLs); rejecting it would break the upload flow.
            if (string.IsNullOrWhiteSpace(req.GuideUrl))
            {
                r.GuideUrl = null;
            }
            else
            {
                string trimmed = req.GuideUrl.Trim();
                bool isServedGuideFile = trimmed.StartsWith("/media/", StringComparison.OrdinalIgnoreCase);
                if (!isServedGuideFile && !UrlValidator.IsValid(trimmed, UrlValidator.WebSchemes))
                {
                    throw new ValidationException(
                        "Guide URL must be a valid absolute http(s) URL.")
                    { Code = ErrorCodes.VenueGuideUrlInvalid };
                }
                r.GuideUrl = trimmed;
            }
        }
        if (req.PhoneNumber != null)
        {
            r.PhoneNumber = ContactFields.NormalizePhone(req.PhoneNumber);
        }
        if (req.EmailAddress != null)
        {
            r.EmailAddress = ContactFields.NormalizeEmail(req.EmailAddress);
        }
        if (req.OpenTime != null)
        {
            r.OpenTime = req.OpenTime;
        }

        if (req.CloseTime != null)
        {
            r.CloseTime = req.CloseTime;
        }

        if (req.OpenDays != null)
        {
            r.OpenDays = req.OpenDays;
        }

        if (req.OpenHours != null)
        {
            OpeningHoursHelper.ApplyOpenHours(r, req.OpenHours);
        }

        if (req.Timezone != null)
        {
            r.Timezone = req.Timezone;
        }

        if (req.Tags != null)
        {
            r.Tags = req.Tags;
        }

        if (req.DefaultBookingDurationMinutes.HasValue)
        {
            if (!BookingDuration.AllowedMinutes.Contains(req.DefaultBookingDurationMinutes.Value))
            {
                throw new ValidationException(
                    $"DefaultBookingDurationMinutes must be one of: {string.Join(", ", BookingDuration.AllowedMinutes.Order())}.")
                {
                    Code = ErrorCodes.VenueDurationInvalid,
                    Args = new Dictionary<string, object> { ["allowed"] = string.Join(", ", BookingDuration.AllowedMinutes.Order()) }
                };
            }

            r.DefaultBookingDurationMinutes = req.DefaultBookingDurationMinutes.Value;
        }

        if (req.DurationRules != null)
        {
            DurationRulesHelper.Apply(r, req.DurationRules);
        }

        if (req.BookingSlotIntervalMinutes.HasValue)
        {
            if (!_allowedBookingSlotIntervalsMinutes.Contains(req.BookingSlotIntervalMinutes.Value))
            {
                throw new ValidationException(
                    $"BookingSlotIntervalMinutes must be one of: {string.Join(", ", _allowedBookingSlotIntervalsMinutes.Order())}.")
                {
                    Code = ErrorCodes.VenueSlotIntervalInvalid,
                    Args = new Dictionary<string, object> { ["allowed"] = string.Join(", ", _allowedBookingSlotIntervalsMinutes.Order()) }
                };
            }

            r.BookingSlotIntervalMinutes = req.BookingSlotIntervalMinutes.Value;
        }

        // Assigned unconditionally: null means "off", and the settings form always sends the
        // field, so a conditional write would make selecting "Off" unable to clear a set cap.
        if (req.MaxSpareCapacity.HasValue && req.MaxSpareCapacity.Value < 0)
        {
            throw new ValidationException("MaxSpareCapacity must be zero or greater, or null to disable.") { Code = ErrorCodes.VenueOversizeCapInvalid };
        }

        r.MaxSpareCapacity = req.MaxSpareCapacity;

        // Same unconditional write as the oversize cap: null is "no cap".
        if (req.MaxGuestsPerSlot is < 1)
        {
            throw new ValidationException("MaxGuestsPerSlot must be 1 or more, or null for no cap.") { Code = ErrorCodes.VenueMaxGuestsInvalid };
        }

        r.MaxGuestsPerSlot = req.MaxGuestsPerSlot;

        if (req.BookingRefFormat != null)
        {
            r.BookingRefFormat = ParseBookingRefFormat(req.BookingRefFormat);
        }

        if (req.WalkInOnly.HasValue)
        {
            r.WalkInOnly = req.WalkInOnly.Value;
        }

        if (req.WalkInDays != null)
        {
            r.WalkInDays = WalkInHelper.NormalizeWalkInDays(req.WalkInDays);
        }

        await _venueRepository.SaveChangesAsync();

        RecordVenueChanges(before, VenueFields.From(r));
        DescribeVenue(AuditActions.VenueUpdate, r, $"Updated the location \"{r.Name}\"");

        return new VenueDto
        {
            Id = r.Id,
            Name = r.Name,
            Address = r.Address,
            OpenTime = r.OpenTime,
            CloseTime = r.CloseTime,
            OpenHours = OpeningHoursHelper.ResolveWeek(r),
            OpenDays = r.OpenDays,
            Timezone = r.Timezone,
            Tags = string.IsNullOrEmpty(r.Tags)
                ? []
                : r.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ImageUrl = r.ImageUrl,
            Description = r.Description,
            GuideUrl = r.GuideUrl,
            PhoneNumber = r.PhoneNumber,
            EmailAddress = r.EmailAddress,
            WalkInOnly = r.WalkInOnly,
            WalkInDays = r.WalkInDays ?? "",
            DefaultBookingDurationMinutes = r.DefaultBookingDurationMinutes,
            DurationRules = DurationRulesHelper.Parse(r.DurationRulesJson),
            BookingSlotIntervalMinutes = r.BookingSlotIntervalMinutes,
            MaxSpareCapacity = r.MaxSpareCapacity,
            MaxGuestsPerSlot = r.MaxGuestsPerSlot,
            BookingRefFormat = r.BookingRefFormat.ToString(),
            Sections = []
        };
    }

    /// <summary>The location settings an admin edit can move, snapshotted either side of the save.</summary>
    private sealed record VenueFields(
        string Name,
        string? Address,
        string? Description,
        string? GuideUrl,
        string? PhoneNumber,
        string? EmailAddress,
        string OpenTime,
        string CloseTime,
        string? OpenHoursJson,
        string OpenDays,
        string Timezone,
        string? Tags,
        int DefaultBookingDurationMinutes,
        string? DurationRulesJson,
        int BookingSlotIntervalMinutes,
        int? MaxSpareCapacity,
        int? MaxGuestsPerSlot,
        BookingRefFormat BookingRefFormat,
        bool WalkInOnly,
        string? WalkInDays)
    {
        public static VenueFields From(Venue r) => new(
            r.Name, r.Address, r.Description, r.GuideUrl, r.PhoneNumber, r.EmailAddress, r.OpenTime,
            r.CloseTime, r.OpenHoursJson, r.OpenDays, r.Timezone, r.Tags,
            r.DefaultBookingDurationMinutes, r.DurationRulesJson, r.BookingSlotIntervalMinutes, r.MaxSpareCapacity,
            r.MaxGuestsPerSlot, r.BookingRefFormat, r.WalkInOnly, r.WalkInDays);
    }

    private void RecordVenueChanges(VenueFields before, VenueFields after)
    {
        _audit.RecordChange("name", before.Name, after.Name);
        _audit.RecordChange("address", before.Address, after.Address);
        _audit.RecordChange("description", before.Description, after.Description);
        _audit.RecordChange("guideUrl", before.GuideUrl, after.GuideUrl);
        _audit.RecordChange("phoneNumber", before.PhoneNumber, after.PhoneNumber);
        _audit.RecordChange("emailAddress", before.EmailAddress, after.EmailAddress);
        _audit.RecordChange("openTime", before.OpenTime, after.OpenTime);
        _audit.RecordChange("closeTime", before.CloseTime, after.CloseTime);
        _audit.RecordChange("openHours", before.OpenHoursJson, after.OpenHoursJson);
        _audit.RecordChange("openDays", before.OpenDays, after.OpenDays);
        _audit.RecordChange("timezone", before.Timezone, after.Timezone);
        _audit.RecordChange("tags", before.Tags, after.Tags);
        _audit.RecordChange("defaultBookingDurationMinutes",
            before.DefaultBookingDurationMinutes, after.DefaultBookingDurationMinutes);
        _audit.RecordChange("durationRules", before.DurationRulesJson, after.DurationRulesJson);
        _audit.RecordChange("bookingSlotIntervalMinutes",
            before.BookingSlotIntervalMinutes, after.BookingSlotIntervalMinutes);
        _audit.RecordChange("maxSpareCapacity",
            before.MaxSpareCapacity, after.MaxSpareCapacity);
        _audit.RecordChange("maxGuestsPerSlot", before.MaxGuestsPerSlot, after.MaxGuestsPerSlot);
        _audit.RecordChange("bookingRefFormat", before.BookingRefFormat, after.BookingRefFormat);
        _audit.RecordChange("walkInOnly", before.WalkInOnly, after.WalkInOnly);
        _audit.RecordChange("walkInDays", before.WalkInDays, after.WalkInDays);
    }

    private void DescribeVenue(string action, Venue venue, string summary)
        => _audit.Describe(action, AuditTargets.Venue, AuditTargets.IdOf(venue.Id),
            venue.Name, venue.Id, summary);

    // ── Sections ────────────────────────────────────────────────────────────

    public async Task<SectionDto?> AddSectionAsync(int venueId, string name)
    {
        Venue? r = await _venueRepository.FindByIdAsync(venueId);
        if (r == null)
        {
            return null;
        }

        int nextSortOrder = await _sectionRepository.CountByVenueAsync(venueId);
        var section = new Section { Name = name, VenueId = venueId, SortOrder = nextSortOrder };
        await _sectionRepository.AddAsync(section);

        DescribeSection(AuditActions.SectionCreate, section, venueId,
            $"Added the section \"{section.Name}\" to {r.Name}");
        return new SectionDto { Id = section.Id, Name = section.Name, SortOrder = section.SortOrder, Resources = [] };
    }

    public async Task<SectionDto?> UpdateSectionAsync(int venueId, int sectionId, string name)
    {
        Section? section = await _sectionRepository.FindForVenueAsync(sectionId, venueId);

        if (section == null)
        {
            return null;
        }

        _audit.RecordChange("name", section.Name, name);
        section.Name = name;
        await _sectionRepository.SaveChangesAsync();

        DescribeSection(AuditActions.SectionUpdate, section, venueId,
            $"Renamed a section to \"{section.Name}\"");
        return new SectionDto { Id = section.Id, Name = section.Name, SortOrder = section.SortOrder, Resources = [] };
    }

    public async Task<bool> DeleteSectionAsync(int venueId, int sectionId)
    {
        Section? section = await _sectionRepository.GetWithResourcesForVenueAsync(sectionId, venueId);

        if (section == null)
        {
            return false;
        }

        // FK-nulled before the section goes: bookings outlive the section they were booked in.
        var resourceIds = section.Resources.Select(t => t.Id).ToList();
        List<Booking> affected = await _bookingRepository.GetBySectionOrResourcesAsync(sectionId, resourceIds);
        foreach (Booking b in affected)
        {
            b.ResourceId = null;
            b.SectionId = null;
        }

        // Every resource in the section goes with it, so any combinable group that spans one of them
        // must be dissolved or shrunk first (see ReconcileResourceGroupsAsync).
        await ReconcileResourceGroupsAsync(venueId, resourceIds);

        _sectionRepository.Remove(section);
        await _sectionRepository.SaveChangesAsync();

        DescribeSection(AuditActions.SectionDelete, section, venueId,
            $"Deleted the section \"{section.Name}\" and its {resourceIds.Count} resources");
        return true;
    }

    private void DescribeSection(string action, Section section, int venueId, string summary)
        => _audit.Describe(action, AuditTargets.Section, AuditTargets.IdOf(section.Id), section.Name,
            venueId, summary);

    // ── Resources ──────────────────────────────────────────────────────────────

    public async Task<ResourceDto?> AddResourceAsync(int venueId, int sectionId, string? name, int capacity, bool walkInOnly = false)
    {
        Section? section = await _sectionRepository.FindForVenueAsync(sectionId, venueId);

        if (section == null)
        {
            return null;
        }

        ValidateCapacity(capacity);

        var resource = new Resource { Name = name, Capacity = capacity, SectionId = sectionId, WalkInOnly = walkInOnly };
        await _resourceRepository.AddAsync(resource);

        DescribeResource(AuditActions.ResourceCreate, resource, venueId,
            $"Added {ResourceLabel(resource)} (capacity {capacity}) to \"{section.Name}\"");
        return ToResourceDto(resource);
    }

    public async Task<ResourceDto?> UpdateResourceAsync(int venueId, int sectionId, int resourceId, string? name, int capacity, bool walkInOnly = false)
    {
        Resource? resource = await _resourceRepository.GetForVenueAsync(resourceId, sectionId, venueId);

        if (resource == null)
        {
            return null;
        }

        ValidateCapacity(capacity);

        _audit.RecordChange("name", resource.Name, name);
        _audit.RecordChange("capacity", resource.Capacity, capacity);
        _audit.RecordChange("walkInOnly", resource.WalkInOnly, walkInOnly);

        resource.Name = name;
        resource.Capacity = capacity;
        resource.WalkInOnly = walkInOnly;
        await _resourceRepository.SaveChangesAsync();

        // After the save, so the reconcile reads the new capacity rather than the pre-edit one.
        await ReconcileResourceGroupsAsync(venueId, Array.Empty<int>());
        await _resourceRepository.SaveChangesAsync();

        DescribeResource(AuditActions.ResourceUpdate, resource, venueId,
            $"Updated {ResourceLabel(resource)} (capacity {resource.Capacity})");
        return ToResourceDto(resource);
    }

    /// <summary>
    /// Restores the invariants a combinable group must satisfy after its member resources changed
    /// underneath it — a member deleted (the DB cascade drops the membership row silently) or
    /// resized. A group left with fewer than two members is dissolved, FK-nulling its bookings first
    /// exactly like <see cref="DeleteResourceGroupAsync"/>; a surviving group has its stored
    /// <see cref="ResourceGroup.CombinedCapacity"/> clamped back into the range
    /// <see cref="ValidateCombinedCapacity"/> enforces on write. Without this a group keeps advertising
    /// combined capacity that no longer exists — resources 8+9 still offered with capacity 8 after resource 9 is
    /// deleted or shrunk, and the booking engine would happily assign a party of 8 to one 4-place resource.
    /// Does not save: the caller's SaveChangesAsync flushes this in the same unit of work.
    /// </summary>
    private async Task ReconcileResourceGroupsAsync(int venueId, IReadOnlyCollection<int> removedResourceIds)
    {
        List<ResourceGroup> groups = await _resourceGroupRepository.GetAllWithMembersByVenueAsync(venueId);

        foreach (ResourceGroup group in groups)
        {
            List<ResourceGroupMembership> remaining = group.Members
                .Where(m => !removedResourceIds.Contains(m.ResourceId))
                .ToList();

            if (remaining.Count < 2)
            {
                foreach (Booking booking in await _bookingRepository.GetByResourceGroupAsync(group.Id))
                {
                    booking.ResourceGroupId = null;
                }

                _resourceGroupRepository.Remove(group);
                continue;
            }

            group.Members = remaining;

            var memberCapacity = remaining.Select(m => m.Resource?.Capacity ?? 0).ToList();
            if (memberCapacity.Any(s => s <= 0))
            {
                continue;
            }

            int floor = memberCapacity.Max() + 1;
            int ceiling = Math.Min(memberCapacity.Sum(), BookingLimits.MaxPartySize);
            if (floor <= ceiling)
            {
                group.CombinedCapacity = Math.Clamp(group.CombinedCapacity, floor, ceiling);
            }
        }
    }

    /// <summary>
    /// Validates a resource/group capacity. Defense in depth behind the DTO [Range] annotation
    /// — a direct service call (e.g. from a seeder or test) bypasses model binding, so this catches
    /// 0/negative/oversized values at the service boundary too.
    /// </summary>
    private static void ValidateCapacity(int capacity)
    {
        if (capacity < BookingLimits.MinPartySize || capacity > BookingLimits.MaxPartySize)
        {
            throw new ValidationException(
                $"Capacity must be between {BookingLimits.MinPartySize} and {BookingLimits.MaxPartySize}.")
            { Code = ErrorCodes.ResourceCapacityOutOfRange, Args = new Dictionary<string, object> { ["min"] = BookingLimits.MinPartySize, ["max"] = BookingLimits.MaxPartySize } };
        }
    }

    /// <summary>
    /// Validates a combinable group's CombinedCapacity: absolute bounds first, then the contextual
    /// window the member set implies. Split from <see cref="ValidateCapacity"/> because that window
    /// depends on the resolved members. The ceiling is the sum of member capacities — combining resources
    /// can only lose places (a shared corner disappears), never invent them. The floor is
    /// one more than the largest member, since a group that fits no more than its biggest resource on
    /// its own is not worth combining and would shadow that resource in the candidate ordering.
    /// </summary>
    private static void ValidateCombinedCapacity(int combinedCapacity, IReadOnlyCollection<Resource> members)
    {
        ValidateCapacity(combinedCapacity);

        int memberCapacitySum = members.Sum(t => t.Capacity);
        if (combinedCapacity > memberCapacitySum)
        {
            throw new ValidationException(
                $"CombinedCapacity ({combinedCapacity}) cannot exceed the sum of member capacities ({memberCapacitySum}).")
            { Code = ErrorCodes.ResourceGroupCombinedCapacityExceedsSum, Args = new Dictionary<string, object> { ["combined"] = combinedCapacity, ["sum"] = memberCapacitySum } };
        }

        int largestMemberCapacity = members.Max(t => t.Capacity);
        if (combinedCapacity <= largestMemberCapacity)
        {
            throw new ValidationException(
                $"CombinedCapacity ({combinedCapacity}) must be more than the largest member resource ({largestMemberCapacity}). "
                + "combining these resources would not fit a bigger party.")
            {
                Code = ErrorCodes.ResourceGroupCombinedCapacityNotWorthCombining,
                Args = new Dictionary<string, object> { ["combined"] = combinedCapacity, ["largest"] = largestMemberCapacity }
            };
        }
    }

    public async Task<bool> DeleteResourceAsync(int venueId, int sectionId, int resourceId)
    {
        Resource? resource = await _resourceRepository.GetForVenueAsync(resourceId, sectionId, venueId);

        if (resource == null)
        {
            return false;
        }

        List<Booking> affected = await _bookingRepository.GetByResourceAsync(resourceId);
        foreach (Booking b in affected)
            b.ResourceId = null;

        // The membership row would be cascade-deleted silently, leaving a combinable group that
        // advertises capacity it can no longer offer. Reconcile before the resource goes.
        await ReconcileResourceGroupsAsync(venueId, new[] { resourceId });

        _resourceRepository.Remove(resource);
        await _resourceRepository.SaveChangesAsync();

        DescribeResource(AuditActions.ResourceDelete, resource, venueId,
            $"Deleted {ResourceLabel(resource)}, clearing it from {affected.Count} bookings");
        return true;
    }

    private void DescribeResource(string action, Resource resource, int venueId, string summary)
        => _audit.Describe(action, AuditTargets.Resource, AuditTargets.IdOf(resource.Id), ResourceLabel(resource),
            venueId, summary);

    private static string ResourceLabel(Resource resource) => resource.Name ?? $"Resource {resource.Id}";

    // ── Delete-impact reads (two-step delete friction) ─────────────────────
    //
    // Cheap, best-effort previews of what a delete would orphan — used by the admin UI to show the
    // consequence ("N future bookings will lose their resource reference") inside the inline confirm step.
    // Counts non-cancelled bookings starting at/after now; past + cancelled bookings are irrelevant to
    // the decision. Mirrors the ownership scoping of the deletes themselves so the count matches the
    // FK-null set the delete will actually touch. Returns null when the resource/section doesn't exist
    // (or doesn't belong to the venue), so the UI can fall back to generic copy.

    public async Task<DeleteImpactDto?> GetResourceDeleteImpactAsync(int venueId, int sectionId, int resourceId)
    {
        Resource? resource = await _resourceRepository.GetForVenueAsync(resourceId, sectionId, venueId);
        if (resource == null)
        {
            return null;
        }

        DateTime nowUtc = DateTime.UtcNow;
        int bookings = await _bookingRepository.CountFutureByResourceAsync(resourceId, nowUtc)
            + await CountFutureGroupBookingsForResourcesAsync(venueId, new[] { resourceId }, nowUtc);

        return new DeleteImpactDto { Bookings = bookings };
    }

    /// <summary>
    /// Upcoming bookings that the location's <em>current</em> schedule would no longer accept.
    /// Narrowing opening hours, closing a day or switching to walk-in only never touches the
    /// bookings already on the books, so this read is the only thing that surfaces the guests
    /// left stranded by the edit. Returns null when the venue doesn't exist.
    /// </summary>
    /// <seealso>VenueManagementServiceTests.GetScheduleConflictsAsync_FlagsBookingsLeftOutsideNarrowedHours</seealso>
    /// <seealso>VenueManagementServiceTests.GetScheduleConflictsAsync_IgnoresBookingsThatStillFit</seealso>
    public async Task<List<ScheduleConflictDto>?> GetScheduleConflictsAsync(int venueId)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId);
        if (venue == null)
        {
            return null;
        }

        List<Booking> upcoming = await _bookingRepository.GetFutureForVenueAsync(venueId, DateTime.UtcNow);

        List<ScheduleConflictDto> conflicts = ScheduleConflictHelper.Conflicting(venue, upcoming)
            .Select(x => new ScheduleConflictDto
            {
                BookingId = x.Booking.Id,
                BookingRef = x.Booking.BookingRef,
                CustomerName = x.Booking.CustomerName,
                Date = x.Booking.Date,
                PartySize = x.Booking.PartySize,
                Reason = ReasonKey(x.Reason),
            })
            .ToList();
        return BookingGuestVisibility.Apply(conflicts, _currentUser);
    }

    private static string ReasonKey(ScheduleConflictReason reason) => reason switch
    {
        ScheduleConflictReason.ClosedDay => "closedDay",
        ScheduleConflictReason.OutsideHours => "outsideHours",
        // Unreachable: GetScheduleConflictsAsync filters None out before mapping.
        _ => "unknown",
    };

    public async Task<DeleteImpactDto?> GetSectionDeleteImpactAsync(int venueId, int sectionId)
    {
        Section? section = await _sectionRepository.GetWithResourcesForVenueAsync(sectionId, venueId);
        if (section == null)
        {
            return null;
        }

        DateTime nowUtc = DateTime.UtcNow;
        var resourceIds = section.Resources.Select(t => t.Id).ToList();
        int bookings = await _bookingRepository.CountFutureBySectionOrResourcesAsync(sectionId, resourceIds, nowUtc)
            + await CountFutureGroupBookingsForResourcesAsync(venueId, resourceIds, nowUtc);

        return new DeleteImpactDto { Bookings = bookings };
    }

    /// <summary>
    /// Upcoming bookings that reserve <paramref name="resourceIds"/> through a combinable group rather than
    /// directly. Those rows carry ResourceId = null, so the resource/section impact counts miss them entirely and
    /// the admin's confirm step would claim a delete orphans nothing while a merged-resource party is booked.
    /// </summary>
    private async Task<int> CountFutureGroupBookingsForResourcesAsync(
        int venueId,
        IReadOnlyCollection<int> resourceIds,
        DateTime nowUtc)
    {
        List<ResourceGroup> groups = await _resourceGroupRepository.GetAllWithMembersByVenueAsync(venueId);
        var affectedGroupIds = groups
            .Where(g => g.Members.Any(m => resourceIds.Contains(m.ResourceId)))
            .Select(g => g.Id)
            .ToList();

        return affectedGroupIds.Count == 0
            ? 0
            : await _bookingRepository.CountFutureByResourceGroupsAsync(affectedGroupIds, nowUtc);
    }

    // ── Combinable resource groups ────────────────────────────────────────────
    //
    // A group is a first-class bookable unit made of combined physical resources. Members must
    // all belong to the same venue, no member may already be in another group, and the stored
    // CombinedCapacity must sit between "more than the largest member" and "the sum of the members"
    // (combining resources commonly loses a place where the corners meet). All rules are enforced
    // here in-service because the in-memory provider used by tests ignores SQL constraints; the
    // unique index on ResourceId is the production backstop.

    public async Task<ResourceGroupDto?> AddResourceGroupAsync(int venueId, CreateResourceGroupRequest req)
    {
        bool exists = await _venueRepository.ExistsAsync(venueId);
        if (!exists)
        {
            return null;
        }

        List<Resource> members = await ResolveAndValidateMembersAsync(venueId, req.Members, excludeGroupId: null);

        // CombinedCapacity must be within bounds and inside the window the member set implies.
        ValidateCombinedCapacity(req.CombinedCapacity, members);

        var group = new ResourceGroup
        {
            Name = req.Name,
            VenueId = venueId,
            CombinedCapacity = req.CombinedCapacity,
            Members = members.Select(t => new ResourceGroupMembership { ResourceId = t.Id, Resource = t }).ToList()
        };

        await _resourceGroupRepository.AddAsync(group);
        await _resourceGroupRepository.SaveChangesAsync();

        DescribeResourceGroup(AuditActions.ResourceGroupCreate, group,
            $"Made {BookingMapper.GroupLabel(group)} combinable, capacity {group.CombinedCapacity}");
        return ToGroupDto(group);
    }

    public async Task<ResourceGroupDto?> UpdateResourceGroupAsync(int venueId, int groupId, UpdateResourceGroupRequest req)
    {
        ResourceGroup? group = await _resourceGroupRepository.GetByIdWithMembersAsync(groupId, venueId);
        if (group == null)
        {
            return null;
        }

        // Re-validate the new member set, allowing the group's own current members to stay.
        var currentMemberIds = group.Members.Select(m => m.ResourceId).ToHashSet();
        List<Resource> members = await ResolveAndValidateMembersAsync(
            venueId, req.Members, excludeGroupId: groupId, currentMemberIds: currentMemberIds);

        ValidateCombinedCapacity(req.CombinedCapacity, members);

        _audit.RecordChange("name", group.Name, req.Name);
        _audit.RecordChange("combinedCapacity", group.CombinedCapacity, req.CombinedCapacity);
        _audit.RecordChange("members", MemberIdList(currentMemberIds), MemberIdList(members.Select(t => t.Id)));

        group.Name = req.Name;
        group.CombinedCapacity = req.CombinedCapacity;
        // Replace the member set in place so EF tracks the add/remove diff and cascades correctly.
        group.Members = members.Select(t => new ResourceGroupMembership { ResourceGroupId = group.Id, ResourceId = t.Id, Resource = t }).ToList();

        await _resourceGroupRepository.SaveChangesAsync();

        DescribeResourceGroup(AuditActions.ResourceGroupUpdate, group,
            $"Updated {BookingMapper.GroupLabel(group)}, capacity {group.CombinedCapacity}");
        return ToGroupDto(group);
    }

    public async Task<bool> DeleteResourceGroupAsync(int venueId, int groupId)
    {
        ResourceGroup? group = await _resourceGroupRepository.GetByIdWithMembersAsync(groupId, venueId);
        if (group == null)
        {
            return false;
        }

        // Bookings keep every other detail; only the group reference is cleared.
        List<Booking> affected = await _bookingRepository.GetByResourceGroupAsync(groupId);
        foreach (Booking b in affected)
        {
            b.ResourceGroupId = null;
        }

        _resourceGroupRepository.Remove(group);
        await _resourceGroupRepository.SaveChangesAsync();

        DescribeResourceGroup(AuditActions.ResourceGroupDelete, group,
            $"Split up {BookingMapper.GroupLabel(group)}, freeing {affected.Count} bookings from it");
        return true;
    }

    private void DescribeResourceGroup(string action, ResourceGroup group, string summary)
        => _audit.Describe(action, AuditTargets.ResourceGroup, AuditTargets.IdOf(group.Id),
            BookingMapper.GroupLabel(group), group.VenueId, summary);

    private static string MemberIdList(IEnumerable<int> resourceIds)
        => string.Join(",", resourceIds.Order());

    /// <summary>
    /// Loads the requested member resources, scoped to the venue, and enforces every data-integrity
    /// rule: distinct ids, &gt;= 2 members, all exist + belong to this venue, and none already in
    /// another group (allowing a resource to remain in the group being edited). Throws
    /// <see cref="ValidationException"/> on the first violation.
    /// </summary>
    private async Task<List<Resource>> ResolveAndValidateMembersAsync(
        int venueId,
        IReadOnlyList<int> memberIds,
        int? excludeGroupId,
        HashSet<int>? currentMemberIds = null)
    {
        if (memberIds == null || memberIds.Count < 2)
        {
            throw new ValidationException("A combinable resource group must have at least two members.") { Code = ErrorCodes.ResourceGroupMinMembers };
        }

        if (memberIds.Distinct().Count() != memberIds.Count)
        {
            throw new ValidationException("A combinable resource group cannot list the same resource twice.") { Code = ErrorCodes.ResourceGroupDuplicateMember };
        }

        // Load candidate members scoped to the venue via the section→venue chain.
        List<Resource> resources = await _resourceRepository.GetManyForVenueAsync(memberIds, venueId);
        if (resources.Count != memberIds.Count)
        {
            throw new ValidationException("All member resources must exist and belong to this venue.") { Code = ErrorCodes.ResourceGroupInvalidMembers };
        }

        // Reject any member already claimed by a *different* group. A resource in the group being edited
        // (currentMemberIds) is allowed to stay; everything else must be ungrouped.
        var grouped = await _resourceGroupRepository.GetAllWithMembersByVenueAsync(venueId);
        var resourceIdToGroup = new Dictionary<int, int>();
        foreach (ResourceGroup g in grouped)
        {
            if (excludeGroupId.HasValue && g.Id == excludeGroupId.Value) continue;
            foreach (ResourceGroupMembership m in g.Members)
            {
                resourceIdToGroup[m.ResourceId] = g.Id;
            }
        }

        foreach (Resource t in resources)
        {
            if (resourceIdToGroup.TryGetValue(t.Id, out int ownerGroupId))
            {
                throw new ValidationException(
                    $"Resource {t.Id} already belongs to another combinable group ({ownerGroupId}).")
                { Code = ErrorCodes.ResourceGroupMemberAlreadyGrouped, Args = new Dictionary<string, object> { ["resourceId"] = t.Id, ["groupId"] = ownerGroupId } };
            }
        }

        return resources;
    }

    private static ResourceGroupDto ToGroupDto(ResourceGroup g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        CombinedCapacity = g.CombinedCapacity,
        Members = g.Members
            .OrderBy(m => m.ResourceId)
            .Select(m => m.Resource ?? throw new InvalidOperationException(
                $"ResourceGroupMembership {m.ResourceGroupId}/{m.ResourceId} has no Resource loaded."))
            .Select(ToResourceDto)
            .ToList()
    };

    // ── Mapping ─────────────────────────────────────────────────────────────

    private static ResourceDto ToResourceDto(Resource t)
        => new() { Id = t.Id, Name = t.Name, Capacity = t.Capacity, WalkInOnly = t.WalkInOnly };

    private static VenueDto ToDto(Venue r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Address = r.Address,
        OpenTime = r.OpenTime,
        CloseTime = r.CloseTime,
        OpenHours = OpeningHoursHelper.ResolveWeek(r),
        OpenDays = r.OpenDays,
        Timezone = r.Timezone,
        Tags = string.IsNullOrEmpty(r.Tags)
            ? []
            : r.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ImageUrl = r.ImageUrl,
        Description = r.Description,
        GuideUrl = r.GuideUrl,
        PhoneNumber = r.PhoneNumber,
        EmailAddress = r.EmailAddress,
        IsArchived = r.IsArchived,
        WalkInOnly = r.WalkInOnly,
        WalkInDays = r.WalkInDays ?? "",
        DefaultBookingDurationMinutes = r.DefaultBookingDurationMinutes,
        DurationRules = DurationRulesHelper.Parse(r.DurationRulesJson),
        BookingSlotIntervalMinutes = r.BookingSlotIntervalMinutes,
        MaxSpareCapacity = r.MaxSpareCapacity,
        MaxGuestsPerSlot = r.MaxGuestsPerSlot,
        BookingRefFormat = r.BookingRefFormat.ToString(),
        Sections = r.Sections
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => new SectionDto
            {
                Id = s.Id,
                Name = s.Name,
                SortOrder = s.SortOrder,
                Resources = s.Resources.Select(ToResourceDto).ToList()
            }).ToList(),
        Groups = (r.Groups ?? Enumerable.Empty<ResourceGroup>())
            .OrderBy(g => g.Id)
            .Select(g => new ResourceGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                CombinedCapacity = g.CombinedCapacity,
                Members = (g.Members ?? Enumerable.Empty<ResourceGroupMembership>())
                    .OrderBy(m => m.ResourceId)
                    .Select(m => ToResourceDto(m.Resource!))
                    .ToList()
            }).ToList()
    };
}
