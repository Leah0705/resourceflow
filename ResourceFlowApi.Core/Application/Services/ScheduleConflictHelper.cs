using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Why an already-taken booking no longer fits its location's schedule. Editing opening hours
/// or open days never touches existing rows, so a booking taken under the old schedule survives
/// the edit and is only discoverable by re-evaluating it against the current one.
/// <para>
/// Every reason here means the guest arrives to a closed venue. Switching a location or a
/// day to walk-in-only is deliberately <em>not</em> one: the location is still open and still
/// serving guests, it just stops taking new online bookings, so a booking already on the books
/// is honoured as it stands. Staff-recorded walk-ins are exempt from that gate by design and
/// would otherwise be reported as conflicts on a location that can never clear them.
/// </para>
/// </summary>
/// <seealso>ScheduleConflictHelperTests.Evaluate_KeepsSlotOnAWalkInOnlyDay</seealso>
/// <seealso>ScheduleConflictHelperTests.Evaluate_KeepsSlotAtAWalkInOnlyLocation</seealso>
public enum ScheduleConflictReason
{
    /// <summary>The booking still falls inside a service the location currently runs.</summary>
    None,

    /// <summary>The booking's local day is no longer in <see cref="Venue.OpenDays"/>.</summary>
    ClosedDay,

    /// <summary>The day is open, but the booking starts outside that day's service window.</summary>
    OutsideHours,
}

/// <summary>
/// Re-evaluates an existing booking against the schedule a location has <em>now</em>. Editing
/// hours or open days never touches rows already taken, so a booking sold under the old
/// schedule is only discoverable by asking this again.
/// </summary>
/// <seealso>ScheduleConflictHelperTests.Evaluate_KeepsAfterMidnightSlotOfAnOvernightService</seealso>
/// <seealso>ScheduleConflictHelperTests.Evaluate_FlagsSlotOnADayThatIsNoLongerOpen</seealso>
public static class ScheduleConflictHelper
{
    /// <summary>
    /// The bookings among <paramref name="bookings"/> that <paramref name="venue"/>'s current
    /// schedule would no longer accept, each with the reason. Callers differ in what they do with
    /// the result — the locations panel lists them, the dashboard counts them — but which bookings
    /// are stranded is one question with one answer.
    /// </summary>
    /// <seealso>ScheduleConflictHelperTests.Conflicting_ReturnsOnlyTheBookingsTheScheduleNoLongerAccepts</seealso>
    public static List<(Booking Booking, ScheduleConflictReason Reason)> Conflicting(
        Venue venue,
        IEnumerable<Booking> bookings)
        => bookings
            .Select(b => (Booking: b, Reason: Evaluate(venue, b.Date)))
            .Where(x => x.Reason != ScheduleConflictReason.None)
            .ToList();

    public static ScheduleConflictReason Evaluate(Venue venue, DateTime bookingUtc)
    {
        if (!ServiceWindowHelper.IsServingAt(venue, bookingUtc))
        {
            DateTime local = TimeZoneHelper.ConvertUtcToLocal(bookingUtc, venue.Timezone);
            return ServiceWindowHelper.IsOpenOn(venue, IsoDay.Of(local))
                ? ScheduleConflictReason.OutsideHours
                : ScheduleConflictReason.ClosedDay;
        }

        return ScheduleConflictReason.None;
    }
}
