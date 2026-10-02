namespace ResourceFlowApi.Core.Domain;

/// <summary>
/// A set of physical <see cref="Resource"/>s an admin has flagged as combinable — e.g. resources 8 &amp; 9,
/// which fit 4 each on their own but together can be booked for a party of 5–8 with the resources
/// combined. Modeled as a first-class bookable unit so a <see cref="Booking"/> stays 1:1 with
/// whatever it reserves (group OR single resource), keeping the blast radius of combinable resources inside
/// the candidate-building step rather than rippling through holds/availability/cancellation.
/// </summary>
public class ResourceGroup
{
    public int Id { get; set; }

    /// <summary>
    /// Optional admin-assigned label (e.g. "Window desks"). Null for an unnamed group — the UI
    /// renders a fallback label from the member resource names.
    /// </summary>
    public string? Name { get; set; }

    public int VenueId { get; set; }
    public Venue? Venue { get; set; }

    /// <summary>
    /// Stored, not computed, capacity of the combined resources. Set by the admin on create
    /// because venues commonly lose a place when combining resources (a shared corner
    /// disappears); validated server-side as &gt;= the sum of member capacities to catch obvious mistakes
    /// but allowed to go higher.
    /// </summary>
    public int CombinedCapacity { get; set; }

    /// <summary>Member resources (the combined physical resources). A resource belongs to at most one group.</summary>
    public ICollection<ResourceGroupMembership> Members { get; set; } = new List<ResourceGroupMembership>();

    /// <summary>
    /// True when a member is a <see cref="Resource.WalkInOnly"/> resource, which keeps the whole group
    /// off the online paths. Needs <c>Members.Resource</c> loaded.
    /// </summary>
    public bool HasWalkInOnlyMember() => Members.Any(m => m.Resource?.WalkInOnly == true);
}
