using ResourceFlowApi.Core.Application.Interfaces;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Caps how many holds one client keeps alive at the same time. Placing a hold needs no
/// account, and each hold takes a resource out of availability for its whole lifetime, so without
/// a cap one address could keep every resource in a venue held by placing holds in a loop.
/// The per-IP rate limit does not prevent this, because a few requests a minute are enough to
/// keep a large set of holds alive.
/// <para>
/// The quota is counted after <see cref="IHoldService"/> places the hold, under one lock, so
/// two concurrent requests from the same client cannot both slip under the cap. A hold that
/// has expired or been released stops counting, which <see cref="IHoldService.GetHold"/>
/// answers, so the quota never has to be told about either.
/// </para>
/// </summary>
/// <seealso>HoldClientQuotaTests.TryAdmit_AcceptsHoldsUpToTheCap</seealso>
/// <seealso>HoldClientQuotaTests.TryAdmit_RejectsTheHoldPastTheCap</seealso>
public sealed class HoldClientQuota(IHoldService holdService, int maxActiveHolds)
{
    /// <summary>A guest normally holds one resource; five leaves room for a few open tabs.</summary>
    public const int DefaultMaxActiveHolds = 5;

    /// <summary>Client entries are swept once the map grows past this, so addresses that
    /// never come back do not accumulate.</summary>
    private const int SweepThreshold = 1024;

    private readonly IHoldService _holdService = holdService;
    private readonly Dictionary<string, List<string>> _holdsByClient = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int MaxActiveHolds { get; } = maxActiveHolds;

    /// <summary>
    /// Counts <paramref name="holdId"/> against <paramref name="clientKey"/>. Returns false, and
    /// counts nothing, when the client already has <see cref="MaxActiveHolds"/> live holds; the
    /// caller then releases the new hold.
    /// </summary>
    public bool TryAdmit(string clientKey, string holdId)
    {
        lock (_lock)
        {
            if (_holdsByClient.Count > SweepThreshold)
            {
                SweepIdleClients();
            }

            if (!_holdsByClient.TryGetValue(clientKey, out List<string>? holds))
            {
                holds = [];
                _holdsByClient[clientKey] = holds;
            }

            holds.RemoveAll(id => _holdService.GetHold(id) is null);
            if (holds.Count >= MaxActiveHolds)
            {
                return false;
            }

            holds.Add(holdId);
            return true;
        }
    }

    private void SweepIdleClients()
    {
        foreach (string clientKey in _holdsByClient.Keys.ToList())
        {
            List<string> holds = _holdsByClient[clientKey];
            holds.RemoveAll(id => _holdService.GetHold(id) is null);
            if (holds.Count == 0)
            {
                _holdsByClient.Remove(clientKey);
            }
        }
    }
}
