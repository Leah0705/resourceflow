using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

public class PauseHelperTests
{
    [Fact]
    public void RejectionMessage_NamesTheEndOfTheWindow_InTheVenuesOwnTimezone()
    {
        var venue = new Venue
        {
            Id = 1,
            Name = "T",
            Timezone = "America/New_York",
            // 20:30 UTC is 15:30 in New York (EST) — the guest is told the local time.
            BookingsPausedUntil = new DateTime(2026, 1, 15, 20, 30, 0, DateTimeKind.Utc)
        };

        Assert.Equal("Bookings are paused until 15:30. Please choose a later time.",
            PauseHelper.RejectionMessage(venue));
    }

    [Fact]
    public void RejectionMessage_FallsBackToAGenericLine_WhenNoWindowIsSet()
    {
        var venue = new Venue { Id = 1, Name = "T", Timezone = "UTC" };

        Assert.Equal("Bookings for this venue are currently paused. Please try again later.",
            PauseHelper.RejectionMessage(venue));
    }

    [Fact]
    public void Rejection_WithEndTime_NamesTheTimeAndCarriesItAsAnArg()
    {
        var venue = new Venue
        {
            Id = 1,
            Name = "T",
            Timezone = "America/New_York",
            BookingsPausedUntil = new DateTime(2026, 1, 15, 20, 30, 0, DateTimeKind.Utc)
        };

        PauseHelper.Rejection rejection = PauseHelper.RejectionFor(venue);

        Assert.Equal(ErrorCodes.BookingPaused, rejection.Code);
        Assert.Equal("15:30", rejection.Args!["until"]);
    }

    [Fact]
    public void Rejection_WithoutEndTime_UsesTheIndefiniteCode()
    {
        // A pause with an end and one without are different sentences, so a client rendering its
        // own copy needs different codes — it cannot branch on an argument being absent.
        var venue = new Venue { Id = 1, Name = "T", Timezone = "UTC" };

        PauseHelper.Rejection rejection = PauseHelper.RejectionFor(venue);

        Assert.Equal(ErrorCodes.BookingPausedIndefinitely, rejection.Code);
        Assert.Null(rejection.Args);
    }
}
