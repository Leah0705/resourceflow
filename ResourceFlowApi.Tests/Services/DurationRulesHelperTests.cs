using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

public class DurationRulesHelperTests
{
    private static DurationRuleDto Rule(int minPartySize, int minutes) => new() { MinPartySize = minPartySize, Minutes = minutes };

    [Fact]
    public void Apply_StoresTheRules_OrderedByPartySize()
    {
        var venue = new Venue();

        DurationRulesHelper.Apply(venue, [Rule(5, 120), Rule(1, 60)]);

        Assert.Equal("""[{"minPartySize":1,"minutes":60},{"minPartySize":5,"minutes":120}]""", venue.DurationRulesJson);
    }

    [Fact]
    public void Apply_ClearsTheRules_GivenAnEmptyList()
    {
        var venue = new Venue { DurationRulesJson = """[{"minPartySize":1,"minutes":60}]""" };

        DurationRulesHelper.Apply(venue, []);

        Assert.Null(venue.DurationRulesJson);
    }

    [Fact]
    public void Apply_Accepts_MinutesFromTheAllowedDurations()
    {
        var venue = new Venue();

        DurationRulesHelper.Apply(venue, [Rule(1, 480)]);

        Assert.NotNull(venue.DurationRulesJson);
    }

    [Fact]
    public void Apply_Rejects_MinutesOutsideTheAllowedDurations()
    {
        ValidationException ex = Assert.Throws<ValidationException>(
            () => DurationRulesHelper.Apply(new Venue(), [Rule(1, 75)]));

        Assert.Equal(ErrorCodes.VenueDurationRuleMinutesInvalid, ex.Code);
    }

    [Fact]
    public void Apply_Accepts_APartySizeAtTheBookingLimit()
    {
        var venue = new Venue();

        DurationRulesHelper.Apply(venue, [Rule(BookingLimits.MaxPartySize, 120)]);

        Assert.NotNull(venue.DurationRulesJson);
    }

    [Fact]
    public void Apply_Rejects_APartySizeAboveTheBookingLimit()
    {
        ValidationException ex = Assert.Throws<ValidationException>(
            () => DurationRulesHelper.Apply(new Venue(), [Rule(BookingLimits.MaxPartySize + 1, 120)]));

        Assert.Equal(ErrorCodes.VenueDurationRulePartySizeOutOfRange, ex.Code);
    }

    [Fact]
    public void Apply_Rejects_APartySizeOfZero()
    {
        ValidationException ex = Assert.Throws<ValidationException>(
            () => DurationRulesHelper.Apply(new Venue(), [Rule(0, 60)]));

        Assert.Equal(ErrorCodes.VenueDurationRulePartySizeOutOfRange, ex.Code);
    }

    [Fact]
    public void Apply_Rejects_TwoRulesForTheSamePartySize()
    {
        ValidationException ex = Assert.Throws<ValidationException>(
            () => DurationRulesHelper.Apply(new Venue(), [Rule(3, 90), Rule(3, 120)]));

        Assert.Equal(ErrorCodes.VenueDurationRuleDuplicatePartySize, ex.Code);
    }

    [Fact]
    public void Parse_ReturnsNoRules_ForUnreadableJson()
    {
        Assert.Empty(DurationRulesHelper.Parse("{not json"));
    }
}
