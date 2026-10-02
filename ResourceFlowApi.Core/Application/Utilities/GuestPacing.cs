using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Guest pacing: how many more guests may start in a slot under
/// <see cref="Venue.MaxGuestsPerSlot"/>. A slot is one step of the availability grid,
/// <c>[slotStart, slotStart + BookingSlotIntervalMinutes)</c>, and its guests are the party sizes of the
/// non-cancelled bookings starting inside it. Availability and booking creation both answer
/// through here, or a slot offered as open would be refused on submit.
/// </summary>
public static class GuestPacing
{
    /// <summary>
    /// Guest places left in the slot starting at <paramref name="slotStartUtc"/>, never below zero, or
    /// null when the location has no cap.
    /// </summary>
    /// <seealso>GuestPacingTests.Remaining_CountsOnlyBookingsStartingInTheSlot</seealso>
    /// <seealso>GuestPacingTests.Remaining_IsNull_WithoutACap</seealso>
    public static int? Remaining(Venue venue, IEnumerable<Booking> bookings, DateTime slotStartUtc)
    {
        if (venue.MaxGuestsPerSlot is not int cap)
        {
            return null;
        }

        DateTime slotEndUtc = slotStartUtc.AddMinutes(AvailabilityService.SlotInterval(venue));
        int guests = bookings
            .Where(b => !b.IsCancelled && b.Date >= slotStartUtc && b.Date < slotEndUtc)
            .Sum(b => b.PartySize);
        return Math.Max(0, cap - guests);
    }

    /// <summary>
    /// Guests per slot for <paramref name="bookings"/>, earliest first, leaving out empty slots and
    /// cancelled bookings. Uses the same slot grid as the cap, so what the dashboard shows is what
    /// booking creation enforces.
    /// </summary>
    /// <seealso>GuestPacingTests.GuestsBySlot_GroupsStartsIntoTheirSlots</seealso>
    public static List<(DateTime SlotStartUtc, int Guests)> GuestsBySlot(Venue venue, IEnumerable<Booking> bookings)
        => bookings
            .Where(b => !b.IsCancelled)
            .GroupBy(b => SlotStartUtc(venue, b.Date))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(b => b.PartySize)))
            .ToList();

    /// <summary>
    /// The start of the availability slot <paramref name="bookingUtc"/> falls in. The grid runs
    /// from the opening of the service the slot belongs to, so a slot after midnight is
    /// measured from the previous day's opening. A client that posts an off-grid time still lands
    /// in the slot availability counts it against.
    /// </summary>
    /// <seealso>GuestPacingTests.SlotStartUtc_FloorsAnOffGridTimeToItsSlot</seealso>
    /// <seealso>GuestPacingTests.SlotStartUtc_MeasuresAnAfterMidnightSlotFromThePreviousOpening</seealso>
    public static DateTime SlotStartUtc(Venue venue, DateTime bookingUtc)
    {
        DateTime local = TimeZoneHelper.ConvertUtcToLocal(bookingUtc, venue.Timezone);
        DateTime open = ServiceWindowHelper.LocalWindowFor(venue, local.Date, IsoDay.Of(local.Date)).Start;
        if (local < open)
        {
            DateTime dayBefore = local.Date.AddDays(-1);
            open = ServiceWindowHelper.LocalWindowFor(venue, dayBefore, IsoDay.Of(dayBefore)).Start;
        }

        int interval = AvailabilityService.SlotInterval(venue);
        int steps = (int)Math.Floor((local - open).TotalMinutes / interval);
        return TimeZoneHelper.ConvertLocalToUtc(open.AddMinutes(steps * interval), venue.Timezone);
    }
}
