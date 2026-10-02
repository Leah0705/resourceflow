using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Predicts when each party in a waitlist gets a resource by replaying the queue against the floor.
/// A resource is free from the end of whatever slot occupies it now; each party, in queue order,
/// takes the fitting unit that frees up first, and holds it for its own slot length. A later
/// party can therefore be quoted a shorter wait than an earlier one when a small resource frees up
/// before a large one, which is how a front desk actually assigns walk-ins.
/// </summary>
public static class WaitEstimator
{
    private sealed record Unit(int Capacity, bool IsGroup, IReadOnlyList<int> ResourceIds);

    /// <summary>
    /// When a party gets a resource. <paramref name="FreeResourceHeldBy"/> is the index of the first party
    /// ahead that the replay gave a fitting, currently free resource to, when that is why this party waits.
    /// </summary>
    public sealed record Estimate(DateTime StartAt, int? FreeResourceHeldBy);

    /// <summary>
    /// The estimate for each of <paramref name="partySizes"/>, in the same order, or null
    /// for a party no resource or group at the location can fit. <paramref name="resourceFreeAtUtc"/>
    /// maps each occupied resource to when it frees up; a resource missing from it is free now.
    /// </summary>
    /// <seealso>WaitEstimatorTests.Estimate_StartsAtOnce_WhenAFittingResourceIsFree</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_WaitsForTheSlotThatEndsFirst</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_QueuesASecondPartyBehindTheFirst</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_LetsASmallPartyOvertakeALargeOne</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_IsNull_ForAPartyNothingCanFit</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_UsesAGroup_OnlyOnceAllItsMembersAreFree</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_RespectsTheOversizeCap</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_HoldsEachResourceForThePartysOwnDurationRule</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_NamesThePartyAhead_HoldingAFreeResourceThePartyFits</seealso>
    /// <seealso>WaitEstimatorTests.Estimate_NamesNoOne_WhenThePartyWaitsOnlyForBusyResources</seealso>
    public static IReadOnlyList<Estimate?> EstimateStartTimes(
        Venue venue,
        IReadOnlyList<int> partySizes,
        IReadOnlyDictionary<int, DateTime> resourceFreeAtUtc,
        DateTime nowUtc)
    {
        List<Unit> units = UnitsOf(venue);
        var freeAt = new Dictionary<int, DateTime>(resourceFreeAtUtc);
        var heldBy = new Dictionary<int, int>();

        var estimates = new List<Estimate?>(partySizes.Count);
        for (int party = 0; party < partySizes.Count; party++)
        {
            int partySize = partySizes[party];
            var fitting = units
                .Where(u => venue.CanFit(u.Capacity, partySize))
                .Select(u => (Unit: u, Start: FreeFrom(u, freeAt, nowUtc)))
                .OrderBy(c => c.Start)
                .ThenBy(c => c.Unit.Capacity)
                .ThenBy(c => c.Unit.IsGroup)
                .ToList();

            if (fitting.Count == 0)
            {
                estimates.Add(null);
                continue;
            }

            var chosen = fitting[0];
            int? freeResourceHeldBy = chosen.Start > nowUtc
                ? fitting
                    .Where(c => FreeFrom(c.Unit, resourceFreeAtUtc, nowUtc) == nowUtc)
                    .SelectMany(c => c.Unit.ResourceIds)
                    .Where(heldBy.ContainsKey)
                    .Min(resourceId => (int?)heldBy[resourceId])
                : null;

            foreach (int resourceId in chosen.Unit.ResourceIds)
            {
                freeAt[resourceId] = chosen.Start.AddMinutes(BookingDuration.For(venue, partySize));
                heldBy.TryAdd(resourceId, party);
            }
            estimates.Add(new Estimate(chosen.Start, freeResourceHeldBy));
        }

        return estimates;
    }

    /// <summary>
    /// When each resource in <paramref name="inProgress"/> frees up: the latest end among the
    /// slots on it, counting a group slot against every member resource.
    /// </summary>
    /// <seealso>WaitEstimatorTests.ResourceFreeTimes_CountsAGroupSlotAgainstEveryMember</seealso>
    /// <seealso>WaitEstimatorTests.ResourceFreeTimes_FallsBackToTheDefaultSlot_WithoutAnEndTime</seealso>
    public static Dictionary<int, DateTime> ResourceFreeTimes(Venue venue, IEnumerable<Booking> inProgress)
    {
        var freeAt = new Dictionary<int, DateTime>();
        foreach (Booking booking in inProgress)
        {
            DateTime end = booking.EndTime ?? booking.Date.AddMinutes(venue.DefaultBookingDurationMinutes);
            foreach (int resourceId in ResourcesOf(booking, venue))
            {
                freeAt[resourceId] = freeAt.TryGetValue(resourceId, out DateTime existing) && existing > end ? existing : end;
            }
        }
        return freeAt;
    }

    /// <summary>Whole minutes from <paramref name="nowUtc"/> to <paramref name="startAtUtc"/>, rounded up; zero when a resource is free now.</summary>
    public static int? MinutesUntil(DateTime? startAtUtc, DateTime nowUtc)
        => startAtUtc is { } at ? Math.Max(0, (int)Math.Ceiling((at - nowUtc).TotalMinutes)) : null;

    private static DateTime FreeFrom(Unit unit, IReadOnlyDictionary<int, DateTime> freeAt, DateTime nowUtc)
    {
        DateTime latest = nowUtc;
        foreach (int resourceId in unit.ResourceIds)
        {
            if (freeAt.TryGetValue(resourceId, out DateTime at) && at > latest)
            {
                latest = at;
            }
        }
        return latest;
    }

    private static List<Unit> UnitsOf(Venue venue)
    {
        var units = venue.Sections
            .SelectMany(s => s.Resources)
            .Select(t => new Unit(t.Capacity, IsGroup: false, new[] { t.Id }))
            .ToList();

        units.AddRange((venue.Groups ?? Enumerable.Empty<ResourceGroup>())
            .Where(g => g.Members.Count > 0)
            .Select(g => new Unit(g.CombinedCapacity, IsGroup: true, g.Members.Select(m => m.ResourceId).ToList())));

        return units;
    }

    private static IEnumerable<int> ResourcesOf(Booking booking, Venue venue)
    {
        if (booking.ResourceId is { } resourceId)
        {
            return new[] { resourceId };
        }

        ResourceGroup? group = venue.Groups?.FirstOrDefault(g => g.Id == booking.ResourceGroupId);
        return group?.Members.Select(m => m.ResourceId) ?? Enumerable.Empty<int>();
    }
}
