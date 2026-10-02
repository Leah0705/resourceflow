using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>
/// Outcome of validating a hold request against venue policy (open hours,
/// walk-in days, pause window, past-date guard) and any existing confirmed
/// booking on the same resource. The controller maps <see cref="HoldPolicyStatus"/>
/// to HTTP status codes; the service has no HTTP surface.
/// </summary>
/// <summary>
/// <see cref="Rejected"/> covers policy violations (past date, paused, walk-in, closed) that map
/// to HTTP 400. <see cref="Booked"/> is a distinct outcome for a resource that isn't free to take (an
/// existing confirmed booking, or a walk-in-only resource) — it maps to HTTP 409, preserving the
/// controller's original status code.
/// </summary>
public enum HoldPolicyStatus { Eligible, NotFound, Rejected, Booked }

/// <summary>
/// Eligible holds carry the resolved <see cref="Venue"/> and the timezone-normalized
/// UTC booking date so the caller can place the hold without re-fetching. Rejected/Booked/
/// NotFound results carry <see cref="FailureMessage"/> instead, plus the matching
/// <see cref="ErrorCodes"/> value in <see cref="Code"/> — this service never throws for an
/// expected policy violation, so the code has to travel on the result rather than on an exception.
/// </summary>
public record HoldPolicyResult(
    HoldPolicyStatus Status,
    Venue? Venue = null,
    DateTime BookingDate = default,
    string? FailureMessage = null,
    string? Code = null,
    IReadOnlyDictionary<string, object>? Args = null)
{
    public static HoldPolicyResult NotFound() => new(HoldPolicyStatus.NotFound, Code: ErrorCodes.VenueNotFound);
    public static HoldPolicyResult Rejected(string message, string? code = null, IReadOnlyDictionary<string, object>? args = null)
        => new(HoldPolicyStatus.Rejected, FailureMessage: message, Code: code, Args: args);
    public static HoldPolicyResult Booked(string message, string? code = null, IReadOnlyDictionary<string, object>? args = null)
        => new(HoldPolicyStatus.Booked, FailureMessage: message, Code: code, Args: args);
    public static HoldPolicyResult Eligible(Venue venue, DateTime bookingDate)
        => new(HoldPolicyStatus.Eligible, venue, bookingDate);
}

/// <summary>
/// Pre-flight validation for a resource hold: resolves the venue, normalizes the
/// requested date to UTC using the venue's timezone, and enforces the same
/// open-hours / walk-in / pause / past-date rules as <c>BookingService</c>, then
/// checks for a conflicting confirmed booking. Delegates timezone parsing to
/// <c>TimeZoneHelper</c>, opening-hours resolution to <c>OpeningHoursHelper</c>,
/// and walk-in policy to <c>WalkInHelper</c>.
/// </summary>
public interface IHoldPolicyService
{
    /// <summary>
    /// Validates the hold request. Returns <see cref="HoldPolicyStatus.Eligible"/> with the
    /// resolved venue + UTC booking date only when every policy check passes AND no
    /// confirmed booking overlaps the resource for a party of <paramref name="partySize"/>'s slot.
    /// Never throws for expected policy violations.
    /// </summary>
    Task<HoldPolicyResult> ValidateAsync(int venueId, int resourceId, DateTime requestedDate, int partySize);

    /// <summary>
    /// Validates an "Any section" / auto-assign hold request. Runs the same venue-level
    /// policy checks as <see cref="ValidateAsync"/> (venue exists, timezone-normalize,
    /// past-date, pause, walk-in, operating hours) but skips the per-resource booking check —
    /// the caller computes the candidate pool separately and the hold placement happens
    /// atomically inside <c>HoldService</c>. Returns <see cref="HoldPolicyStatus.Eligible"/>
    /// with the resolved venue + UTC booking date.
    /// </summary>
    Task<HoldPolicyResult> ValidateAnyResourceAsync(int venueId, DateTime requestedDate);
}
