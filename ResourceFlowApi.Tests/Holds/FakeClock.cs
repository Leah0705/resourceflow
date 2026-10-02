using ResourceFlowApi.Core.Application.Interfaces;

namespace ResourceFlowApi.Tests.Holds;

internal sealed class FakeClock(DateTime initial) : ISystemClock
{
    public DateTime UtcNow { get; set; } = initial;

    public void Advance(TimeSpan by) => UtcNow += by;
}
