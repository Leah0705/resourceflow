namespace ResourceFlowApi.Core.Domain;

/// <summary>
/// Join row linking a <see cref="ResourceGroup"/> to one of its member <see cref="Resource"/>s. Composite
/// PK <c>(ResourceGroupId, ResourceId)</c>; <see cref="ResourceId"/> carries a unique index so a resource can
/// belong to at most one group (also enforced in service code, since the test suite runs on EF
/// In-Memory which ignores SQL constraints).
/// </summary>
public class ResourceGroupMembership
{
    public int ResourceGroupId { get; set; }
    public ResourceGroup? Group { get; set; }

    public int ResourceId { get; set; }
    public Resource? Resource { get; set; }
}
