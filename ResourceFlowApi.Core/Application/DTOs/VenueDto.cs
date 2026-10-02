using System.ComponentModel.DataAnnotations;
using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Core.Application.DTOs;

// ── Request types ──────────────────────────────────────────────────────────

public class UpdateVenueRequest
{
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
    public string? OpenDays { get; set; }

    /// <summary>
    /// Optional freeform blurb shown on the public location detail page. Supports basic
    /// markdown-style inline links. Empty string clears the field; null leaves it untouched.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional link to this location's guide (PDF, page, etc.). Empty string clears the
    /// field; null leaves it untouched.
    /// </summary>
    public string? GuideUrl { get; set; }

    /// <summary>
    /// Optional contact phone number for this location, overriding the global brand phone.
    /// Empty string clears the field; null leaves it untouched.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Optional contact email address for this location, overriding the global brand email.
    /// Empty string clears the field; null leaves it untouched.
    /// </summary>
    public string? EmailAddress { get; set; }

    /// <summary>
    /// Per-day opening hours (ISO day 1=Monday … 7=Sunday). When provided, takes
    /// precedence over OpenTime/CloseTime; identical hours for all 7 days collapse
    /// back into the uniform OpenTime/CloseTime pair.
    /// </summary>
    public List<DayHoursDto>? OpenHours { get; set; }

    public string? Timezone { get; set; }
    public string? Tags { get; set; }
    public int? DefaultBookingDurationMinutes { get; set; }

    /// <summary>
    /// Slot lengths by party size. Null leaves the stored rules untouched; an empty list
    /// clears them so every party gets the default duration.
    /// </summary>
    public List<DurationRuleDto>? DurationRules { get; set; }

    /// <summary>
    /// Step, in minutes, between selectable booking start times. Null leaves the stored
    /// value untouched (PATCH-style); validated server-side against the allowed set (15/30/60).
    /// </summary>
    public int? BookingSlotIntervalMinutes { get; set; }

    /// <summary>
    /// Max allowed <c>resource.Capacity - partySize</c> before a resource is considered too large to
    /// offer/book. Null clears the setting (unrestricted). Always assigned from the settings
    /// form so "Off" can be re-saved; validated server-side as non-negative.
    /// </summary>
    public int? MaxSpareCapacity { get; set; }

    /// <summary>
    /// Most guests whose online bookings may start in one slot. Null clears the cap. Always
    /// assigned from the settings form, like <see cref="MaxSpareCapacity"/>; validated as 1 or more.
    /// </summary>
    public int? MaxGuestsPerSlot { get; set; }

    /// <summary>
    /// Booking reference format for new bookings — "AlphaNumeric" (three words) or "Numeric"
    /// (digits only), case-insensitive. Null leaves the stored value untouched (PATCH-style).
    /// Sent as a string rather than the enum because the API has no string-enum converter
    /// configured, and a bare int would be an opaque contract for the client.
    /// </summary>
    public string? BookingRefFormat { get; set; }

    /// <summary>When true the whole location becomes walk-in only (no online bookings).</summary>
    public bool? WalkInOnly { get; set; }

    /// <summary>Comma-separated ISO days (1=Monday … 7=Sunday) that are walk-in only. Empty string clears.</summary>
    public string? WalkInDays { get; set; }
}

public class PauseVenueRequest
{
    public int Minutes { get; set; }
}

public class ExtendVenueRequest
{
    public int Minutes { get; set; }
}

public class CreateSectionRequest
{
    public string Name { get; set; } = null!;
}

public class UpdateSectionRequest
{
    public string Name { get; set; } = null!;
}

public class CreateResourceRequest
{
    public string? Name { get; set; }

    [Range(BookingLimits.MinPartySize, BookingLimits.MaxPartySize)]
    public int Capacity { get; set; }

    /// <summary>Keep the resource for walk-ins: never offered or bookable online.</summary>
    public bool WalkInOnly { get; set; }
}

public class UpdateResourceRequest
{
    public string? Name { get; set; }

    [Range(BookingLimits.MinPartySize, BookingLimits.MaxPartySize)]
    public int Capacity { get; set; }

    /// <summary>Keep the resource for walk-ins: never offered or bookable online.</summary>
    public bool WalkInOnly { get; set; }
}

/// <summary>
/// Best-effort preview of what a resource/section delete would orphan — shown in the admin UI's two-step
/// delete confirmation so the owner sees the consequence in concrete terms before destroying the row.
/// <c>Bookings</c> counts non-cancelled <b>future</b> bookings that would lose their resource/section
/// reference (the FK-null the delete itself already performs); past/cancelled bookings don't matter.
/// </summary>
public class DeleteImpactDto
{
    /// <summary>Non-cancelled future bookings that lose their resource/section reference.</summary>
    public int Bookings { get; set; }
}

// ── Schedule conflicts ────────────────────────────────────────────────────

/// <summary>
/// An upcoming booking that no longer fits the location's current schedule, so the admin who
/// narrowed the hours can see who they have to move or call.
/// </summary>
public class ScheduleConflictDto
{
    public int BookingId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string? CustomerName { get; set; }

    /// <summary>Slot start, UTC — the client renders it in the location's timezone.</summary>
    public DateTime Date { get; set; }
    public int PartySize { get; set; }

    /// <summary>"closedDay" | "outsideHours" | "walkInOnly" — see <c>ScheduleConflictReason</c>.</summary>
    public string Reason { get; set; } = string.Empty;
}

// ── Combinable resource groups ───────────────────────────────────────────────

public class CreateResourceGroupRequest
{
    public string? Name { get; set; }

    /// <summary>Ids of the member resources to combine. Must be &gt;= 2, all owned by the venue, none already grouped.</summary>
    public List<int> Members { get; set; } = new();

    /// <summary>Stored combined capacity; validated &gt;= sum of member capacities and &lt;= <see cref="BookingLimits.MaxPartySize"/>.</summary>
    [Range(BookingLimits.MinPartySize, BookingLimits.MaxPartySize)]
    public int CombinedCapacity { get; set; }
}

public class UpdateResourceGroupRequest
{
    public string? Name { get; set; }
    public List<int> Members { get; set; } = new();

    [Range(BookingLimits.MinPartySize, BookingLimits.MaxPartySize)]
    public int CombinedCapacity { get; set; }
}

// ── Response DTOs ──────────────────────────────────────────────────────────

public class ResourceDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int Capacity { get; set; }

    /// <summary>Kept for walk-ins: never offered online, still assignable by staff.</summary>
    public bool WalkInOnly { get; set; }
}

public class SectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public List<ResourceDto> Resources { get; set; } = new();
}

/// <summary>
/// A combinable-resource group: physical resources an admin flagged as combinable, bookable as one
/// unit for larger parties. <c>CombinedCapacity</c> is the stored capacity; <c>Members</c> are the
/// combined physical resources.
/// </summary>
public class ResourceGroupDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int CombinedCapacity { get; set; }
    public List<ResourceDto> Members { get; set; } = new();
}

public class ReorderSectionsRequest
{
    /// <summary>
    /// The venue's sections, in the desired display order. Must contain exactly the
    /// same set of section IDs the venue currently has (no additions/removals here).
    /// </summary>
    public List<int> SectionIds { get; set; } = new();
}

public class DayHoursDto
{
    /// <summary>ISO 8601 day number: 1=Monday … 7=Sunday.</summary>
    public int Day { get; set; }
    public string Open { get; set; } = OpeningHourDefaults.Open;
    public string Close { get; set; } = OpeningHourDefaults.Close;
}

/// <summary>A party of <see cref="MinPartySize"/> or more holds its resource for <see cref="Minutes"/>, up to the next rule.</summary>
public class DurationRuleDto
{
    public int MinPartySize { get; set; }
    public int Minutes { get; set; }
}

public class VenueDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string OpenTime { get; set; } = OpeningHourDefaults.Open;
    public string CloseTime { get; set; } = OpeningHourDefaults.Close;

    /// <summary>Resolved hours for every day of the week (always 7 entries).</summary>
    public List<DayHoursDto> OpenHours { get; set; } = new();

    public string OpenDays { get; set; } = "1,2,3,4,5,6,7";
    public string Timezone { get; set; } = "UTC";
    public string[] Tags { get; set; } = [];
    public string? ImageUrl { get; set; }

    /// <summary>Optional blurb shown on the public location detail page (null when none).</summary>
    public string? Description { get; set; }

    /// <summary>Optional link to this location's guide (null when none).</summary>
    public string? GuideUrl { get; set; }

    /// <summary>Optional contact phone for this location (null when none — callers fall back to the brand phone).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Optional contact email for this location (null when none — callers fall back to the brand email).</summary>
    public string? EmailAddress { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>When true the whole location is walk-in only — the booking flow is disabled.</summary>
    public bool WalkInOnly { get; set; }

    /// <summary>Comma-separated ISO days (1=Monday … 7=Sunday) that are walk-in only ("" when none).</summary>
    public string WalkInDays { get; set; } = "";

    public int DefaultBookingDurationMinutes { get; set; } = 60;

    /// <summary>Slot lengths by party size, smallest party first. Empty when every party gets the default.</summary>
    public List<DurationRuleDto> DurationRules { get; set; } = new();

    /// <summary>Step (minutes) between selectable booking start times (default 30).</summary>
    public int BookingSlotIntervalMinutes { get; set; } = 30;

    /// <summary>Max allowed spare capacity over party size, or null for unrestricted (off).</summary>
    public int? MaxSpareCapacity { get; set; }

    /// <summary>Most guests whose online bookings may start in one slot, or null for no cap.</summary>
    public int? MaxGuestsPerSlot { get; set; }

    /// <summary>Format of references minted for new bookings: "AlphaNumeric" or "Numeric".</summary>
    public string BookingRefFormat { get; set; } = Domain.BookingRefFormat.AlphaNumeric.ToString();

    public List<SectionDto> Sections { get; set; } = new();

    /// <summary>Combinable-resource groups defined for this venue. Empty when none.</summary>
    public List<ResourceGroupDto> Groups { get; set; } = new();
}
