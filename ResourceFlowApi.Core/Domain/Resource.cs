namespace ResourceFlowApi.Core.Domain;

public class Resource
{
    public int Id { get; set; }
    public string? Name { get; set; }

    public int Capacity { get; set; }

    /// <summary>
    /// Held back for the front desk: never offered or bookable online, but staff can still assign a
    /// party here from the admin or the waitlist. A combinable group with a walk-in-only member
    /// is held back with it, or an online group booking would take the resource anyway.
    /// </summary>
    public bool WalkInOnly { get; set; }

    // Relation to Section
    public int SectionId { get; set; }
    public Section? Section { get; set; }
}
