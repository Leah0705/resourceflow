using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Computes the ordered candidate list for "Any section" auto-assignment, shared by the holds
/// controller and <see cref="BookingService"/>. This class only builds the pre-sorted pool; the
/// atomic pick happens inside <see cref="IHoldService.PlaceAutoHold"/>'s lock, so a candidate
/// here is a proposal, not a reservation.
/// </summary>
public sealed class ResourceAutoAssigner(
    IBookingRepository bookingRepository,
    IHoldService holdService)
{
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IHoldService _holdService = holdService;

    /// <summary>
    /// The venue's bookable units that can fit <paramref name="partySize"/> (see
    /// <see cref="Venue.CanFit"/>) and carry no overlapping booking or hold at
    /// <paramref name="bookingDateUtc"/>, ordered smallest-fitting-first and then by
    /// <see cref="DeprioritizationTier"/>. Empty when nothing fits. Walk-in-only resources, and groups
    /// holding one, are left out unless <paramref name="includeWalkInOnly"/> is set, which only the
    /// waitlist does: every other caller is placing an online booking.
    /// </summary>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_PrefersStandaloneResource_WhenPartyFitsBoth</seealso>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_OffersGroupOnly_WhenNoStandaloneResourceFits</seealso>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_SkipsWalkInOnlyResourcesAndGroups_Online</seealso>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_OffersWalkInOnlyResourcesAndGroups_ToTheWaitlist</seealso>
    public async Task<IReadOnlyList<ResourceCandidate>> BuildCandidatesAsync(
        Venue venue,
        int partySize,
        DateTime bookingDateUtc,
        bool includeWalkInOnly = false)
    {
        if (venue.Sections is null || venue.Sections.Count == 0 || partySize <= 0)
        {
            return Array.Empty<ResourceCandidate>();
        }

        int durationMinutes = BookingDuration.For(venue, partySize);
        var free = new List<ResourceCandidate>();
        free.AddRange(await FreeResourcesAsync(venue, partySize, bookingDateUtc, durationMinutes, includeWalkInOnly));
        free.AddRange(await FreeGroupsAsync(venue, partySize, bookingDateUtc, durationMinutes, includeWalkInOnly));

        HashSet<int> groupedResourceIds = GroupedResourceIds(venue);

        return free
            .OrderBy(c => c.Capacity)
            .ThenBy(c => DeprioritizationTier(c, groupedResourceIds))
            .ThenBy(c => c.ResourceId)
            .ToList();
    }

    /// <summary>
    /// An ungrouped resource beats a combinable resource of the same size, which beats a group of that
    /// size, so combinable resources stay free as long as possible for the larger parties that need
    /// them combined. Grouping a resource only moves it down this order — it never leaves the
    /// individual pool, because a resource in a group still offers its own capacity on its own.
    /// </summary>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_OffersGroupedResourcesIndividually_AfterUngroupedOnesOfTheSameSize</seealso>
    /// <seealso>ResourceAutoAssignerTests.BuildCandidates_DeprioritizesGroups_AgainstStandaloneResourcesOfEqualCapacity</seealso>
    private static int DeprioritizationTier(ResourceCandidate candidate, HashSet<int> groupedResourceIds)
        => candidate.IsGroup ? 2 : groupedResourceIds.Contains(candidate.ResourceId) ? 1 : 0;

    private static HashSet<int> GroupedResourceIds(Venue venue)
        => (venue.Groups ?? Enumerable.Empty<ResourceGroup>())
            .SelectMany(g => g.Members).Select(m => m.ResourceId).ToHashSet();

    /// <summary>
    /// Checked serially because the repository answers one unit at a time. The pool is a single
    /// venue's resources and this runs once per auto-assign request, before the lock.
    /// </summary>
    private async Task<List<ResourceCandidate>> FreeResourcesAsync(
        Venue venue, int partySize, DateTime bookingDateUtc, int durationMinutes, bool includeWalkInOnly)
    {
        var free = new List<ResourceCandidate>();

        IEnumerable<(Resource Resource, int SectionId)> eligible = venue.Sections!
            .Where(s => s.Resources != null)
            .SelectMany(s => s.Resources!.Where(t => t != null).Select(t => (Resource: t!, SectionId: s.Id)))
            .Where(x => (includeWalkInOnly || !x.Resource.WalkInOnly) && venue.CanFit(x.Resource.Capacity, partySize));

        foreach ((Resource resource, int sectionId) in eligible)
        {
            bool booked = await _bookingRepository.IsUnitBookedOnDateAsync(
                resource.Id, resourceGroupId: null, bookingDateUtc, durationMinutes);
            if (!booked && !_holdService.IsResourceHeld(resource.Id, bookingDateUtc, durationMinutes: durationMinutes))
            {
                free.Add(new ResourceCandidate(resource.Id, sectionId, resource.Capacity));
            }
        }

        return free;
    }

    private async Task<List<ResourceCandidate>> FreeGroupsAsync(
        Venue venue, int partySize, DateTime bookingDateUtc, int durationMinutes, bool includeWalkInOnly)
    {
        var free = new List<ResourceCandidate>();
        if (venue.Groups is not { Count: > 0 })
        {
            return free;
        }

        foreach (ResourceGroup group in venue.Groups)
        {
            if (!venue.CanFit(group.CombinedCapacity, partySize)) continue;
            if (!includeWalkInOnly && group.HasWalkInOnlyMember()) continue;

            var memberIds = group.Members.Select(m => m.ResourceId).ToList();
            if (memberIds.Count == 0) continue;

            // One query covers all three ways a group can be taken: the group itself booked, a
            // member booked individually, or a member reserved by a sibling group's booking.
            bool groupBooked = await _bookingRepository.IsUnitBookedOnDateAsync(
                resourceId: null, resourceGroupId: group.Id, bookingDateUtc, durationMinutes);
            if (groupBooked) continue;

            bool anyMemberHeld = memberIds.Any(
                id => _holdService.IsResourceHeld(id, bookingDateUtc, durationMinutes: durationMinutes));
            if (anyMemberHeld) continue;

            // Anchored to the lowest member's id/section so callers reading ResourceId/SectionId still
            // get a concrete value; the group identity rides on ResourceGroupId + MemberResourceIds.
            ResourceGroupMembership anchor = group.Members.OrderBy(m => m.ResourceId).First();
            free.Add(new ResourceCandidate(
                anchor.ResourceId,
                anchor.Resource?.SectionId ?? 0,
                group.CombinedCapacity,
                IsGroup: true,
                ResourceGroupId: group.Id,
                MemberResourceIds: memberIds));
        }

        return free;
    }
}
