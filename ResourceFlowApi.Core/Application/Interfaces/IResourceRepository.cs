using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface IResourceRepository
{
    Task<Resource?> GetByIdAsync(int id);

    // ── Bundle 2 additions ───────────────────────────────────────────────────

    /// <summary>Bare lookup by id with NO includes.</summary>
    Task<Resource?> FindByIdAsync(int id);

    /// <summary>
    /// Loads the resource with its <see cref="Resource.Section"/> (and that section's <see cref="Section.Venue"/>)
    /// eager-loaded, restricted to a specific section id — used by AdminService.CreateBookingAsync to
    /// validate the resource belongs to the requested section.
    /// </summary>
    Task<Resource?> GetWithSectionVenueAsync(int resourceId, int sectionId);

    /// <summary>
    /// Loads the resource with its <see cref="Resource.Section"/> only, filtered by id and venue id.
    /// Used by AdminService.AdminUpdateBookingAsync to validate a reassignment target.
    /// </summary>
    Task<Resource?> GetWithSectionForVenueAsync(int resourceId, int venueId);

    /// <summary>Total resources in a venue (across all sections) — used by the capacity notification.</summary>
    Task<int> CountByVenueAsync(int venueId);

    /// <summary>Adds a resource.</summary>
    Task AddAsync(Resource resource);

    /// <summary>Removes a resource.</summary>
    void Remove(Resource resource);

    /// <summary>
    /// Filtered lookup by (resourceId, sectionId, venueId) with <see cref="Resource.Section"/> eager-loaded —
    /// used by VenueManagementService update/delete-resource to validate the resource's ownership.
    /// </summary>
    Task<Resource?> GetForVenueAsync(int resourceId, int sectionId, int venueId);

    /// <summary>
    /// Loads the resources whose ids are in <paramref name="resourceIds"/>, scoped to a venue via the
    /// section→venue chain — used by VenueManagementService to resolve + validate combinable
    /// group members. Returns at most one row per id; callers compare counts to detect unknown/cross-
    /// venue ids.
    /// </summary>
    Task<List<Resource>> GetManyForVenueAsync(IReadOnlyList<int> resourceIds, int venueId);

    /// <summary>Flushes pending changes on the underlying DbContext.</summary>
    Task SaveChangesAsync();
}
