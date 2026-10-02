using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface IResourceGroupRepository
{
    /// <summary>
    /// Loads a group with its <see cref="ResourceGroup.Members"/> (and each member's <see cref="Resource"/>)
    /// eager-loaded, restricted to a specific venue — used by update/delete to validate ownership
    /// and mutate the member set. Returns null when the group doesn't exist or belongs elsewhere.
    /// </summary>
    Task<ResourceGroup?> GetByIdWithMembersAsync(int groupId, int venueId);

    /// <summary>
    /// All groups for a venue, with members (and their resources) eager-loaded, ordered by Id for
    /// deterministic DTO output.
    /// </summary>
    Task<List<ResourceGroup>> GetAllWithMembersByVenueAsync(int venueId);

    /// <summary>Adds a group (caller is responsible for SaveChanges).</summary>
    Task AddAsync(ResourceGroup group);

    /// <summary>Removes a group (caller is responsible for SaveChanges; memberships cascade).</summary>
    void Remove(ResourceGroup group);

    /// <summary>Flushes pending changes on the underlying DbContext.</summary>
    Task SaveChangesAsync();
}
