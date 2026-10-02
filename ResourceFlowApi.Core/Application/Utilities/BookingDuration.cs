using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Resolves when a slot actually finishes. <see cref="Booking.EndTime"/> is nullable and rows
/// taken before it existed carry null, so anything that needs the end of a booking has to fall back
/// to the location's default duration rather than read the booking as zero-length.
/// </summary>
/// <seealso>BookingDurationTests.ResolveEnd_UsesStoredEndTime</seealso>
/// <seealso>BookingDurationTests.ResolveEnd_FallsBackToVenueDefaultWhenNull</seealso>
/// <seealso>BookingDurationTests.ResolveEnd_FallsBackWhenStoredEndIsNotAfterStart</seealso>
public static class BookingDuration
{
    /// <summary>Slot length assumed when a location carries no configured default.</summary>
    public const int FallbackMinutes = 60;

    /// <summary>The slot lengths a location may choose, for its default and for each duration rule.</summary>
    public static readonly IReadOnlySet<int> AllowedMinutes =
        new HashSet<int> { 30, 60, 90, 120, 150, 180, 240, 300, 360, 420, 480 };

    /// <summary>
    /// How long a new slot for a party of <paramref name="partySize"/> holds its resource: the duration
    /// rule with the largest party size at or below <paramref name="partySize"/>, else the location's
    /// default. Only for slots being created; an existing booking keeps its stored
    /// <see cref="Booking.EndTime"/>.
    /// </summary>
    /// <seealso>BookingDurationTests.For_UsesTheRuleForTheLargerParty_AtItsBoundary</seealso>
    /// <seealso>BookingDurationTests.For_KeepsTheSmallerRule_OnePersonBelowTheBoundary</seealso>
    /// <seealso>BookingDurationTests.For_UsesTheDefault_BelowTheLowestRule</seealso>
    public static int For(Venue venue, int partySize)
        => DurationRulesHelper.Parse(venue.DurationRulesJson)
            .LastOrDefault(rule => rule.MinPartySize <= partySize)?.Minutes
            ?? venue.DefaultBookingDurationMinutes;

    public static DateTime ResolveEnd(DateTime start, DateTime? endTime, int? defaultDurationMinutes)
        => endTime.HasValue && endTime.Value > start
            ? endTime.Value
            : start.AddMinutes(defaultDurationMinutes ?? FallbackMinutes);
}
