using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Utilities;

public class GuestPacingTests
{
    private static readonly DateTime Seven = new(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);

    private static Venue Paced(int? cap = 10, string open = "17:00", string close = "23:00") => new()
    {
        Name = "Paced", Timezone = "UTC", OpenTime = open, CloseTime = close,
        BookingSlotIntervalMinutes = 30, MaxGuestsPerSlot = cap,
    };

    private static Booking At(DateTime date, int partySize, bool cancelled = false)
        => new() { Date = date, PartySize = partySize, IsCancelled = cancelled };

    [Fact]
    public void Remaining_CountsOnlyBookingsStartingInTheSlot()
    {
        Booking[] bookings =
        [
            At(Seven.AddMinutes(-15), 4), // previous slot
            At(Seven, 2),
            At(Seven.AddMinutes(29), 3),
            At(Seven.AddMinutes(30), 5), // next slot
            At(Seven.AddMinutes(10), 6, cancelled: true),
        ];

        Assert.Equal(5, GuestPacing.Remaining(Paced(), bookings, Seven));
    }

    [Fact]
    public void Remaining_NeverGoesBelowZero()
    {
        Assert.Equal(0, GuestPacing.Remaining(Paced(cap: 4), [At(Seven, 6)], Seven));
    }

    [Fact]
    public void Remaining_IsNull_WithoutACap()
    {
        Assert.Null(GuestPacing.Remaining(Paced(cap: null), [At(Seven, 6)], Seven));
    }

    [Fact]
    public void SlotStartUtc_FloorsAnOffGridTimeToItsSlot()
    {
        // A 17:15 opening puts the grid on quarter past and quarter to, not on the hour.
        Venue venue = Paced(open: "17:15");

        Assert.Equal(Seven.AddMinutes(-15), GuestPacing.SlotStartUtc(venue, Seven));
        Assert.Equal(Seven.AddMinutes(15), GuestPacing.SlotStartUtc(venue, Seven.AddMinutes(15)));
    }

    [Fact]
    public void SlotStartUtc_MeasuresAnAfterMidnightSlotFromThePreviousOpening()
    {
        Venue venue = Paced(open: "18:15", close: "02:00");
        DateTime afterMidnight = new(2026, 10, 11, 0, 50, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 11, 0, 45, 0, DateTimeKind.Utc), GuestPacing.SlotStartUtc(venue, afterMidnight));
    }

    [Fact]
    public void GuestsBySlot_GroupsStartsIntoTheirSlots()
    {
        Booking[] bookings = [At(Seven.AddMinutes(45), 2), At(Seven, 4), At(Seven.AddMinutes(20), 3), At(Seven, 5, cancelled: true)];

        Assert.Equal(
            [(Seven, 7), (Seven.AddMinutes(30), 2)],
            GuestPacing.GuestsBySlot(Paced(), bookings));
    }
}
