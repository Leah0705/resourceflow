using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

public class WaitEstimatorTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>A 2-place resource (id 1) and a 4-place resource (id 2), 60-minute slots.</summary>
    private static Venue Floor(int? oversizeCap = null)
    {
        var venue = new Venue { Id = 1, Name = "R", DefaultBookingDurationMinutes = 60, MaxSpareCapacity = oversizeCap };
        venue.Sections.Add(new Section
        {
            Id = 1,
            Name = "Main",
            VenueId = 1,
            Resources = new List<Resource>
            {
                new() { Id = 1, Capacity = 2, SectionId = 1 },
                new() { Id = 2, Capacity = 4, SectionId = 1 },
            },
        });
        return venue;
    }

    private static IReadOnlyList<DateTime?> Estimate(Venue r, int[] parties, Dictionary<int, DateTime>? freeAt = null)
        => WaitEstimator.EstimateStartTimes(r, parties, freeAt ?? new Dictionary<int, DateTime>(), Now).Select(e => e?.StartAt).ToList();

    private static IReadOnlyList<int?> FreeResourceHeldBy(Venue r, int[] parties, Dictionary<int, DateTime> freeAt)
        => WaitEstimator.EstimateStartTimes(r, parties, freeAt, Now).Select(e => e?.FreeResourceHeldBy).ToList();

    [Fact]
    public void Estimate_StartsAtOnce_WhenAFittingResourceIsFree()
    {
        Assert.Equal(Now, Estimate(Floor(), [2])[0]);
    }

    [Fact]
    public void Estimate_WaitsForTheSlotThatEndsFirst()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(40), [2] = Now.AddMinutes(15) };

        Assert.Equal(Now.AddMinutes(15), Estimate(Floor(), [2], freeAt)[0]);
    }

    [Fact]
    public void Estimate_QueuesASecondPartyBehindTheFirst()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(30) };

        IReadOnlyList<DateTime?> startTimes = Estimate(Floor(), [4, 4], freeAt);

        Assert.Equal(Now, startTimes[0]);
        Assert.Equal(Now.AddMinutes(60), startTimes[1]);
    }

    [Fact]
    public void Estimate_LetsASmallPartyOvertakeALargeOne()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(10), [2] = Now.AddMinutes(45) };

        IReadOnlyList<DateTime?> startTimes = Estimate(Floor(), [4, 2], freeAt);

        Assert.Equal(Now.AddMinutes(45), startTimes[0]);
        Assert.Equal(Now.AddMinutes(10), startTimes[1]);
    }

    [Fact]
    public void Estimate_PrefersTheSmallerResource_WhenBothAreFree()
    {
        IReadOnlyList<DateTime?> startTimes = Estimate(Floor(), [2, 4]);

        Assert.Equal(Now, startTimes[0]);
        Assert.Equal(Now, startTimes[1]);
    }

    [Fact]
    public void Estimate_HoldsEachResourceForThePartysOwnDurationRule()
    {
        Venue floor = Floor();
        floor.DurationRulesJson = """[{"minPartySize":1,"minutes":60},{"minPartySize":3,"minutes":120}]""";

        IReadOnlyList<DateTime?> startTimes = Estimate(floor, [4, 4, 2, 2]);

        Assert.Equal(Now.AddMinutes(120), startTimes[1]);
        Assert.Equal(Now.AddMinutes(60), startTimes[3]);
    }

    [Fact]
    public void Estimate_IsNull_ForAPartyNothingCanFit()
    {
        Assert.Null(Estimate(Floor(), [5])[0]);
    }

    [Fact]
    public void Estimate_UsesAGroup_OnlyOnceAllItsMembersAreFree()
    {
        Venue r = Floor();
        r.Groups.Add(new ResourceGroup
        {
            Id = 9,
            CombinedCapacity = 6,
            Members = new List<ResourceGroupMembership> { new() { ResourceGroupId = 9, ResourceId = 1 }, new() { ResourceGroupId = 9, ResourceId = 2 } },
        });
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(20), [2] = Now.AddMinutes(50) };

        Assert.Equal(Now.AddMinutes(50), Estimate(r, [6], freeAt)[0]);
    }

    [Fact]
    public void Estimate_RespectsTheOversizeCap()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(30) };

        Assert.Equal(Now, Estimate(Floor(oversizeCap: 3), [1], freeAt)[0]);
        Assert.Equal(Now.AddMinutes(30), Estimate(Floor(oversizeCap: 2), [1], freeAt)[0]);
    }

    [Fact]
    public void Estimate_NamesThePartyAhead_HoldingAFreeResourceThePartyFits()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(30) };

        Assert.Equal([null, 0], FreeResourceHeldBy(Floor(), [2, 4], freeAt));
    }

    [Fact]
    public void Estimate_NamesNoOne_WhenThePartyWaitsOnlyForBusyResources()
    {
        var freeAt = new Dictionary<int, DateTime> { [1] = Now.AddMinutes(30), [2] = Now.AddMinutes(15) };

        Assert.Equal([null, null], FreeResourceHeldBy(Floor(), [2, 4], freeAt));
    }

    [Fact]
    public void ResourceFreeTimes_CountsAGroupSlotAgainstEveryMember()
    {
        Venue r = Floor();
        r.Groups.Add(new ResourceGroup
        {
            Id = 9,
            CombinedCapacity = 6,
            Members = new List<ResourceGroupMembership> { new() { ResourceGroupId = 9, ResourceId = 1 }, new() { ResourceGroupId = 9, ResourceId = 2 } },
        });
        var booking = new Booking { ResourceGroupId = 9, Date = Now.AddMinutes(-30), EndTime = Now.AddMinutes(30) };

        Dictionary<int, DateTime> freeAt = WaitEstimator.ResourceFreeTimes(r, [booking]);

        Assert.Equal(Now.AddMinutes(30), freeAt[1]);
        Assert.Equal(Now.AddMinutes(30), freeAt[2]);
    }

    [Fact]
    public void ResourceFreeTimes_FallsBackToTheDefaultSlot_WithoutAnEndTime()
    {
        var booking = new Booking { ResourceId = 1, Date = Now.AddMinutes(-20) };

        Assert.Equal(Now.AddMinutes(40), WaitEstimator.ResourceFreeTimes(Floor(), [booking])[1]);
    }

    [Fact]
    public void ResourceFreeTimes_KeepsTheLatestEnd_WhenSlotsOverlapOnAResource()
    {
        Booking[] bookings =
        [
            new() { ResourceId = 1, Date = Now.AddMinutes(-50), EndTime = Now.AddMinutes(40) },
            new() { ResourceId = 1, Date = Now.AddMinutes(-10), EndTime = Now.AddMinutes(10) },
        ];

        Assert.Equal(Now.AddMinutes(40), WaitEstimator.ResourceFreeTimes(Floor(), bookings)[1]);
    }

    [Fact]
    public void ResourceFreeTimes_IgnoresAGroupBookingForAGroupThatNoLongerExists()
    {
        var booking = new Booking { ResourceGroupId = 42, Date = Now, EndTime = Now.AddMinutes(60) };

        Assert.Empty(WaitEstimator.ResourceFreeTimes(Floor(), [booking]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(0.5, 1)]
    [InlineData(12, 12)]
    public void MinutesUntil_RoundsUp_AndNeverGoesNegative(double minutesAhead, int expected)
    {
        Assert.Equal(expected, WaitEstimator.MinutesUntil(Now.AddMinutes(minutesAhead), Now));
    }

    [Fact]
    public void MinutesUntil_IsNull_WithoutAnEstimate()
    {
        Assert.Null(WaitEstimator.MinutesUntil(null, Now));
    }
}
