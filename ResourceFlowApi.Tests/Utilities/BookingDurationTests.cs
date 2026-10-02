using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Utilities;

public class BookingDurationTests
{
    private static readonly DateTime Start = new(2026, 8, 23, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveEnd_UsesStoredEndTime()
    {
        DateTime stored = Start.AddMinutes(150);

        Assert.Equal(stored, BookingDuration.ResolveEnd(Start, stored, 90));
    }

    [Fact]
    public void ResolveEnd_FallsBackToVenueDefaultWhenNull()
    {
        Assert.Equal(Start.AddMinutes(90), BookingDuration.ResolveEnd(Start, null, 90));
    }

    [Fact]
    public void ResolveEnd_FallsBackWhenStoredEndIsNotAfterStart()
    {
        Assert.Equal(Start.AddMinutes(90), BookingDuration.ResolveEnd(Start, Start, 90));
    }

    [Fact]
    public void ResolveEnd_UsesFallbackMinutesWhenLocationHasNoDefault()
    {
        Assert.Equal(
            Start.AddMinutes(BookingDuration.FallbackMinutes),
            BookingDuration.ResolveEnd(Start, null, null));
    }

    private static Venue WithDurationRules() => new()
    {
        DefaultBookingDurationMinutes = 45,
        DurationRulesJson = """[{"minPartySize":1,"minutes":60},{"minPartySize":3,"minutes":90},{"minPartySize":5,"minutes":120}]""",
    };

    [Fact]
    public void For_UsesTheRuleForTheLargerParty_AtItsBoundary()
    {
        Assert.Equal(90, BookingDuration.For(WithDurationRules(), 3));
    }

    [Fact]
    public void For_KeepsTheSmallerRule_OnePersonBelowTheBoundary()
    {
        Assert.Equal(60, BookingDuration.For(WithDurationRules(), 2));
    }

    [Fact]
    public void For_UsesTheLargestRule_ForAnyBiggerParty()
    {
        Assert.Equal(120, BookingDuration.For(WithDurationRules(), 12));
    }

    [Fact]
    public void For_UsesTheDefault_BelowTheLowestRule()
    {
        var venue = new Venue
        {
            DefaultBookingDurationMinutes = 60,
            DurationRulesJson = """[{"minPartySize":6,"minutes":150}]""",
        };

        Assert.Equal(60, BookingDuration.For(venue, 5));
    }

    [Fact]
    public void For_UsesTheDefault_WithoutRules()
    {
        Assert.Equal(75, BookingDuration.For(new Venue { DefaultBookingDurationMinutes = 75 }, 8));
    }
}
