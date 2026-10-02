using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

public class ScheduleConflictHelperTests
{
    private static Venue Venue(
        string openTime = "11:00",
        string closeTime = "23:00",
        string openDays = "1,2,3,4,5,6,7",
        string? openHoursJson = null,
        bool walkInOnly = false,
        string? walkInDays = null) => new()
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenDays = openDays,
            OpenHoursJson = openHoursJson,
            WalkInOnly = walkInOnly,
            WalkInDays = walkInDays,
        };

    // 2026-08-24 is a Monday, so the ISO day of each instant below is unambiguous.
    private static DateTime MondayAt(int hour, int minute = 0)
        => new(2026, 8, 24, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_KeepsSlotInsideTheCurrentWindow()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(), MondayAt(19)));
    }

    [Fact]
    public void Evaluate_FlagsSlotBeforeTheNarrowedOpeningTime()
    {
        Assert.Equal(
            ScheduleConflictReason.OutsideHours,
            ScheduleConflictHelper.Evaluate(Venue(openTime: "17:00"), MondayAt(12)));
    }

    [Fact]
    public void Evaluate_KeepsSlotAtTheOpeningTimeItself()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(openTime: "17:00"), MondayAt(17)));
    }

    [Fact]
    public void Evaluate_FlagsSlotAtTheNewClosingTime()
    {
        Assert.Equal(
            ScheduleConflictReason.OutsideHours,
            ScheduleConflictHelper.Evaluate(Venue(closeTime: "21:00"), MondayAt(21)));
    }

    [Fact]
    public void Evaluate_KeepsSlotJustBeforeTheNewClosingTime()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(closeTime: "21:00"), MondayAt(20, 59)));
    }

    [Fact]
    public void Evaluate_FlagsSlotOnADayThatIsNoLongerOpen()
    {
        Assert.Equal(
            ScheduleConflictReason.ClosedDay,
            ScheduleConflictHelper.Evaluate(Venue(openDays: "2,3,4,5,6,7"), MondayAt(19)));
    }

    [Fact]
    public void Evaluate_FlagsSlotAgainstThePerDayOverrideRatherThanTheUniformHours()
    {
        // Monday closes at 20:00 through the override; the uniform CloseTime would still allow 21:00.
        Venue venue = Venue(openHoursJson: """{"1":{"open":"11:00","close":"20:00"}}""");

        Assert.Equal(
            ScheduleConflictReason.OutsideHours,
            ScheduleConflictHelper.Evaluate(venue, MondayAt(21)));
    }

    [Fact]
    public void Evaluate_KeepsAfterMidnightSlotOfAnOvernightService()
    {
        // Sunday runs 18:00–02:00, so a 00:30 slot belongs to Sunday's service even though
        // it lands on Monday. AvailabilityService sells that slot; flagging it would be a false
        // positive on every late-night venue.
        Venue venue = Venue(openTime: "18:00", closeTime: "02:00");

        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(venue, MondayAt(0, 30)));
    }

    [Fact]
    public void Evaluate_FlagsAfterMidnightSlotWhenThePreviousDayIsClosed()
    {
        // Same overnight window, but Sunday is no longer an open day — nothing serves 00:30 now.
        Venue venue = Venue(openTime: "18:00", closeTime: "02:00", openDays: "1,2,3,4,5,6");

        Assert.Equal(
            ScheduleConflictReason.OutsideHours,
            ScheduleConflictHelper.Evaluate(venue, MondayAt(0, 30)));
    }

    // The walk-in flag stops the location taking *new* online bookings. It does not close the
    // location, so a slot already on the books is honoured and is not a conflict. Staff can
    // still record walk-ins against such a location (AdminService.CreateBookingAsync is exempt),
    // which would otherwise leave it permanently reporting conflicts it can never clear.
    [Fact]
    public void Evaluate_KeepsSlotAtAWalkInOnlyLocation()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(walkInOnly: true), MondayAt(19)));
    }

    [Fact]
    public void Evaluate_KeepsSlotOnAWalkInOnlyDay()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(walkInDays: "1"), MondayAt(19)));
    }

    [Fact]
    public void Evaluate_StillFlagsAClosedDayAtAWalkInOnlyLocation()
    {
        // The walk-in flag is not a conflict, but it does not excuse one either: this Monday is
        // closed outright, so the guest has nowhere to be served whatever the booking policy is.
        Venue venue = Venue(openDays: "2,3,4,5,6,7", walkInOnly: true);

        Assert.Equal(
            ScheduleConflictReason.ClosedDay,
            ScheduleConflictHelper.Evaluate(venue, MondayAt(19)));
    }

    [Fact]
    public void Evaluate_StillFlagsHoursAtAWalkInOnlyLocation()
    {
        Assert.Equal(
            ScheduleConflictReason.OutsideHours,
            ScheduleConflictHelper.Evaluate(Venue(walkInOnly: true), MondayAt(3)));
    }

    [Fact]
    public void Evaluate_ResolvesTheSlotInTheLocationTimezone()
    {
        // 02:00 UTC is 21:00 the previous day in New York — inside an 11:00–23:00 service.
        // Reading the instant as UTC would place it before opening and flag it.
        Venue venue = Venue();
        venue.Timezone = "America/New_York";

        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(venue, MondayAt(2)));
    }

    [Fact]
    public void Evaluate_KeepsEverySlotWhenOpenAndCloseMatch()
    {
        Assert.Equal(
            ScheduleConflictReason.None,
            ScheduleConflictHelper.Evaluate(Venue(openTime: "00:00", closeTime: "00:00"), MondayAt(4)));
    }

    [Fact]
    public void Conflicting_ReturnsOnlyTheBookingsTheScheduleNoLongerAccepts()
    {
        // Open 11:00-23:00, so the 09:00 slot is stranded and the other two still fit.
        Venue venue = Venue();
        List<Booking> bookings =
        [
            new() { Id = 1, Date = MondayAt(12), BookingRef = "fits" },
            new() { Id = 2, Date = MondayAt(9), BookingRef = "stranded" },
            new() { Id = 3, Date = MondayAt(22), BookingRef = "fits-late" },
        ];

        List<(Booking Booking, ScheduleConflictReason Reason)> conflicts =
            ScheduleConflictHelper.Conflicting(venue, bookings);

        (Booking booking, ScheduleConflictReason reason) = Assert.Single(conflicts);
        Assert.Equal("stranded", booking.BookingRef);
        Assert.Equal(ScheduleConflictReason.OutsideHours, reason);
    }

    [Fact]
    public void Conflicting_ReturnsNothing_WhenEveryBookingStillFits()
    {
        Venue venue = Venue();
        List<Booking> bookings = [new() { Id = 1, Date = MondayAt(12), BookingRef = "fits" }];

        Assert.Empty(ScheduleConflictHelper.Conflicting(venue, bookings));
    }
}
