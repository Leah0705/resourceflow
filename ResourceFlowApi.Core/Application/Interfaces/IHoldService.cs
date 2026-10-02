namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>
/// A placed hold. For a single-resource hold, <see cref="ResourceId"/> is set and <see cref="MemberResourceIds"/>
/// is empty. For a combinable-resource group hold, <see cref="ResourceGroupId"/> is set and
/// <see cref="MemberResourceIds"/> lists every member resource the hold reserves — those resources are all
/// treated as busy (see <see cref="IHoldService.IsResourceHeld"/>). One hold id releases the whole group.
/// <see cref="DurationMinutes"/> is the slot the hold blocks for, which differs between party
/// sizes once a location has duration rules.
/// </summary>
public record HoldEntry(
    string HoldId,
    int ResourceId,
    int SectionId,
    int VenueId,
    DateTime Date,
    DateTime ExpiresAt,
    int? ResourceGroupId = null,
    IReadOnlyList<int>? MemberResourceIds = null,
    int DurationMinutes = 60
)
{
    /// <summary>True when this hold reserves a combinable-resource group rather than one resource.</summary>
    public bool IsGroup => ResourceGroupId.HasValue;

    /// <summary>Member resources reserved by this hold (empty for single-resource holds).</summary>
    public IReadOnlyList<int> Members => MemberResourceIds ?? Array.Empty<int>();
}

public record HoldResult(string HoldId, DateTime ExpiresAt);

/// <summary>
/// A venue-wide candidate for auto-assignment ("Any section"). May be a standalone resource or a
/// combinable-resource group. Callers pre-sort candidates by <see cref="Capacity"/> ascending,
/// then <see cref="IsGroup"/> (standalone resources before groups of equal capacity — the
/// "deprioritize combinable resources" rule), then <see cref="ResourceId"/> for determinism, before
/// passing them in, so the smallest fitting standalone resource wins; a group is only chosen when no
/// standalone resource fits.
/// </summary>
public record ResourceCandidate(
    int ResourceId,
    int SectionId,
    int Capacity,
    bool IsGroup = false,
    int? ResourceGroupId = null,
    IReadOnlyList<int>? MemberResourceIds = null
)
{
    /// <summary>Member resources reserved when this candidate is a group (empty for single-resource).</summary>
    public IReadOnlyList<int> Members => MemberResourceIds ?? Array.Empty<int>();
}

/// <summary>
/// Outcome of an auto-assign hold: the placed hold plus the resource/section (or group + members) the
/// server resolved, so the caller can persist or display the chosen reservation.
/// </summary>
public record AutoAssignResult(
    string HoldId,
    DateTime ExpiresAt,
    int ResourceId,
    int SectionId,
    bool IsGroup = false,
    int? ResourceGroupId = null,
    IReadOnlyList<int>? MemberResourceIds = null
)
{
    public IReadOnlyList<int> Members => MemberResourceIds ?? Array.Empty<int>();
}

public interface IHoldService
{
    HoldResult? PlaceHold(int venueId, int resourceId, int sectionId, DateTime bookingDate, string? currentHoldId = null, int durationMinutes = 60);

    /// <summary>
    /// Place a hold on every member resource of a combinable group as a single atomic entry, so the
    /// whole group is reserved and released together. All members must be free (no other hold
    /// overlaps); returns null otherwise. Mirrors <see cref="PlaceHold"/>'s atomic-replace-current
    /// semantics. The returned <see cref="HoldResult.HoldId"/> identifies one multi-resource hold.
    /// </summary>
    HoldResult? PlaceGroupHold(
        int venueId,
        int resourceGroupId,
        IReadOnlyList<int> memberResourceIds,
        int sectionId,
        DateTime bookingDate,
        string? currentHoldId = null,
        int durationMinutes = 60);

    void ReleaseHold(string holdId);
    /// <summary>
    /// True when an active hold, over its own slot, overlaps the window [<paramref name="bookingDate"/>,
    /// <paramref name="bookingDate"/> + <paramref name="durationMinutes"/>) on this resource —
    /// <b>including</b> a group hold that reserves this resource as one of its members. Pass
    /// <paramref name="excludeHoldId"/> to skip the caller's own hold.
    /// </summary>
    /// <seealso>HoldServiceTests.IsResourceHeld_MeasuresAnExistingHold_ByItsOwnSlot</seealso>
    /// <seealso>HoldServiceTests.IsResourceHeld_False_OnceTheHoldsOwnSlotHasEnded</seealso>
    bool IsResourceHeld(int resourceId, DateTime bookingDate, string? excludeHoldId = null, int durationMinutes = 60);
    HoldEntry? GetHold(string holdId);
    int GetActiveHoldsCount();

    /// <summary>
    /// Auto-assign variant of <see cref="PlaceHold"/> for "Any section" requests. Iterates
    /// <paramref name="candidates"/> (already sorted by the caller: Capacity ascending, IsGroup,
    /// then ResourceId) and places a hold on the first one that is not held by someone else, all
    /// inside the existing placement lock so the pick + hold are atomic. Group candidates place a
    /// single multi-member hold. Returns <c>null</c> if every candidate is taken. The caller's
    /// <paramref name="currentHoldId"/> is excluded from the held-check and atomically replaced,
    /// matching <see cref="PlaceHold"/>.
    /// </summary>
    AutoAssignResult? PlaceAutoHold(
        int venueId,
        IReadOnlyList<ResourceCandidate> candidates,
        DateTime bookingDate,
        string? currentHoldId = null,
        int durationMinutes = 60);
}
