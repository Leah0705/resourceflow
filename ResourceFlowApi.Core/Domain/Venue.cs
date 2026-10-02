using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Core.Domain;

public class Venue
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string OpenTime { get; set; } = OpeningHourDefaults.Open;
    public string CloseTime { get; set; } = OpeningHourDefaults.Close;

    /// <summary>
    /// Comma-separated day numbers (ISO 8601: 1=Monday, 7=Sunday).
    /// Default: all days open. Example: "1,2,3,4,5" = weekdays only.
    /// </summary>
    public string OpenDays { get; set; } = "1,2,3,4,5,6,7";

    /// <summary>
    /// Optional per-day opening hour overrides as JSON keyed by ISO day number,
    /// e.g. {"1":{"open":"12:00","close":"22:00"}}. Null means every day uses
    /// OpenTime/CloseTime. Days missing from the JSON also fall back to them.
    /// </summary>
    public string? OpenHoursJson { get; set; }

    /// <summary>
    /// IANA timezone identifier (e.g. "Europe/London", "America/New_York").
    /// All booking times are interpreted in this timezone.
    /// </summary>
    public string Timezone { get; set; } = "UTC";

    public ICollection<Section> Sections { get; set; } = new List<Section>();

    public ICollection<ResourceGroup> Groups { get; set; } = new List<ResourceGroup>();

    /// <summary>
    /// If set, slots that start before this instant (UTC) are closed to new bookings.
    /// </summary>
    public DateTime? BookingsPausedUntil { get; set; }

    public string? Tags { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// Optional freeform blurb shown on the public location detail page. Supports basic
    /// markdown-style inline links (e.g. "See our [guide](https://example.com/guide)").
    /// Null/empty hides the blurb entirely.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional link to this location's guide — a PDF, a page on their site, wherever it
    /// lives. Null/empty hides the "View guide" affordance on the location page.
    /// </summary>
    public string? GuideUrl { get; set; }

    /// <summary>
    /// Optional contact phone number for this location. Overrides
    /// <see cref="BrandSettings.PhoneNumber"/> wherever a contact is surfaced.
    /// Null/empty falls back to the global value.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Optional contact email address for this location. Overrides
    /// <see cref="BrandSettings.EmailAddress"/>. Null/empty falls back to the global value.
    /// </summary>
    public string? EmailAddress { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>
    /// When true the location accepts walk-ins only: it stays listed publicly
    /// but online bookings (and holds) are rejected for every day.
    /// </summary>
    public bool WalkInOnly { get; set; }

    /// <summary>
    /// Comma-separated ISO day numbers (1=Monday … 7=Sunday) on which the
    /// location accepts walk-ins only. Ignored when <see cref="WalkInOnly"/>
    /// is true (the whole week is walk-in only). Null/empty means bookings
    /// are accepted on every open day.
    /// </summary>
    public string? WalkInDays { get; set; }

    /// <summary>
    /// Length, in minutes, of a single resource's occupancy window for a new booking.
    /// Used wherever a booking's end time is computed (creation, availability, holds).
    /// </summary>
    public int DefaultBookingDurationMinutes { get; set; } = 60;

    /// <summary>
    /// Optional slot lengths by party size as a JSON array, e.g.
    /// [{"minPartySize":1,"minutes":60},{"minPartySize":5,"minutes":120}]. A party gets the rule with the
    /// largest minPartySize at or below its size; null, or a party below every rule, gets
    /// <see cref="DefaultBookingDurationMinutes"/>. Resolve with <c>BookingDuration.For</c>.
    /// </summary>
    public string? DurationRulesJson { get; set; }

    /// <summary>
    /// Step, in minutes, between selectable booking start times. Deliberately independent of
    /// <see cref="DefaultBookingDurationMinutes"/>, so a venue can offer 90-minute
    /// slots that still start every 15 minutes. Constrained to 15/30/60 server-side.
    /// </summary>
    public int BookingSlotIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// When set, the largest spare capacity a resource may carry over the party size and
    /// still be offered — a resource qualifies while
    /// <c>resource.Capacity - partySize &lt;= MaxSpareCapacity</c>. Null is unrestricted. It has
    /// to be applied identically in availability, booking creation/update and auto-assign, or
    /// the customer is offered a resource the server will then refuse. Admin-recorded walk-ins
    /// are intentionally exempt.
    /// </summary>
    public int? MaxSpareCapacity { get; set; }

    /// <summary>
    /// Pacing: the most guests whose online bookings may start in one slot, a slot being
    /// <see cref="BookingSlotIntervalMinutes"/> long. Null is no cap. Only starts count, so a
    /// party that started in the previous slot is not new arrival load. Admin-recorded bookings and
    /// the waitlist are exempt but still count toward the total. Resolve with <c>GuestPacing</c>.
    /// </summary>
    public int? MaxGuestsPerSlot { get; set; }

    /// <summary>
    /// True when a bookable unit with capacity <paramref name="unitCapacity"/> — a resource or a
    /// combinable group — may take a party of <paramref name="partySize"/>: large enough to
    /// fit them, and not so much larger that <see cref="MaxSpareCapacity"/> would rather
    /// hold it for a bigger group.
    /// </summary>
    /// <seealso>VenueTests.CanFit_True_WhenTheUnitIsExactlyAtTheOversizeCap</seealso>
    /// <seealso>VenueTests.CanFit_False_WhenTheUnitIsOnePersonOverTheCap</seealso>
    public bool CanFit(int unitCapacity, int partySize)
        => unitCapacity >= partySize && !ExceedsOversizeCap(unitCapacity, partySize);

    /// <summary>
    /// The upper half of <see cref="CanFit"/>, for the paths that reject a too-small unit
    /// with their own message and only need the cap. Availability, holds, booking
    /// creation/update and auto-assign must all answer this identically: a path that is more
    /// permissive offers the customer a resource the next one then refuses.
    /// </summary>
    public bool ExceedsOversizeCap(int unitCapacity, int partySize)
        => MaxSpareCapacity.HasValue && unitCapacity - partySize > MaxSpareCapacity.Value;

    /// <summary>
    /// Shape of the booking references handed to this location's customers. Only consulted
    /// when a reference is minted, so changing it leaves already-issued references alone.
    /// </summary>
    public BookingRefFormat BookingRefFormat { get; set; } = BookingRefFormat.AlphaNumeric;

    /// <summary>True while a pause is running — for badges and counts, not as a booking gate.</summary>
    public bool IsPaused()
        => BookingsPausedUntil.HasValue && BookingsPausedUntil.Value > DateTime.UtcNow;

    /// <summary>
    /// The booking gate: true when a slot starting at <paramref name="bookingUtc"/> falls
    /// inside the active pause window. Pausing means "stop accepting new arrivals for the next
    /// X hours", so next Saturday stays bookable while tonight's session is paused.
    /// </summary>
    /// <seealso>VenueTests.IsPausedFor_True_WhenTheSlotStartsInsideThePauseWindow</seealso>
    /// <seealso>VenueTests.IsPausedFor_False_WhenTheSlotStartsAfterThePauseWindow</seealso>
    public bool IsPausedFor(DateTime bookingUtc)
        => IsPaused() && bookingUtc < BookingsPausedUntil!.Value;

    public bool IsWalkInOnlyAt(DateTime utc)
        => WalkInHelper.IsWalkInOnlyAt(this, utc);

    /// <summary>
    /// True when a service the venue runs covers the given UTC instant, resolved through
    /// <see cref="Timezone"/> — which falls back to UTC when it is not a known IANA id. A
    /// service that closes after midnight belongs to the day it opened, so an 01:00 slot is
    /// open only if the <em>previous</em> day runs that late.
    /// </summary>
    /// <seealso>VenueTests.IsOpenAt_True_ForOvernightWindow_AfterMidnightClose</seealso>
    /// <seealso>VenueTests.IsOpenAt_False_ForOvernightWindow_OutsideBothSegments</seealso>
    public bool IsOpenAt(DateTime utc) => ServiceWindowHelper.IsServingAt(this, utc);
}
