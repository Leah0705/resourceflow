using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Infrastructure.Holds;

namespace ResourceFlowApi.Tests.Holds;

public class HoldClientQuotaTests
{
    private const string Client = "203.0.113.7";
    private const int Cap = 3;

    private static readonly DateTime _baseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _bookingDate = new(2026, 6, 15, 19, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(_baseTime);
    private readonly HoldService _holds;
    private readonly HoldClientQuota _quota;

    public HoldClientQuotaTests()
    {
        _holds = new HoldService(_clock);
        _quota = new HoldClientQuota(_holds, Cap);
    }

    private string PlaceOn(int resourceId)
        => _holds.PlaceHold(1, resourceId, 1, _bookingDate)!.HoldId;

    [Fact]
    public void TryAdmit_AcceptsHoldsUpToTheCap()
    {
        for (int resourceId = 1; resourceId <= Cap; resourceId++)
        {
            Assert.True(_quota.TryAdmit(Client, PlaceOn(resourceId)));
        }
    }

    [Fact]
    public void TryAdmit_RejectsTheHoldPastTheCap()
    {
        for (int resourceId = 1; resourceId <= Cap; resourceId++)
        {
            _quota.TryAdmit(Client, PlaceOn(resourceId));
        }

        Assert.False(_quota.TryAdmit(Client, PlaceOn(Cap + 1)));
    }

    [Fact]
    public void TryAdmit_CountsEachClientSeparately()
    {
        for (int resourceId = 1; resourceId <= Cap; resourceId++)
        {
            _quota.TryAdmit(Client, PlaceOn(resourceId));
        }

        Assert.True(_quota.TryAdmit("198.51.100.20", PlaceOn(Cap + 1)));
    }

    [Fact]
    public void TryAdmit_StopsCountingAReleasedHold()
    {
        var placed = new List<string>();
        for (int resourceId = 1; resourceId <= Cap; resourceId++)
        {
            string holdId = PlaceOn(resourceId);
            _quota.TryAdmit(Client, holdId);
            placed.Add(holdId);
        }

        _holds.ReleaseHold(placed[0]);

        Assert.True(_quota.TryAdmit(Client, PlaceOn(Cap + 1)));
    }

    [Fact]
    public void TryAdmit_StopsCountingAnExpiredHold()
    {
        for (int resourceId = 1; resourceId <= Cap; resourceId++)
        {
            _quota.TryAdmit(Client, PlaceOn(resourceId));
        }

        _clock.Advance(HoldService.HoldDuration + TimeSpan.FromSeconds(1));

        Assert.True(_quota.TryAdmit(Client, PlaceOn(Cap + 1)));
    }

    [Fact]
    public void TryAdmit_DoesNotCountAHoldThatReplacedTheClientsPreviousOne()
    {
        string first = PlaceOn(1);
        _quota.TryAdmit(Client, first);
        for (int resourceId = 2; resourceId <= Cap; resourceId++)
        {
            _quota.TryAdmit(Client, PlaceOn(resourceId));
        }

        // Moving the first hold to another resource releases it inside HoldService.
        HoldResult? moved = _holds.PlaceHold(1, Cap + 1, 1, _bookingDate, currentHoldId: first);

        Assert.True(_quota.TryAdmit(Client, moved!.HoldId));
    }
}
