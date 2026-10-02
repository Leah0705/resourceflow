using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface ISectionRepository
{
    Task<Section?> GetByIdAsync(int id);

    // ── Bundle 2 additions ───────────────────────────────────────────────────

    /// <summary>Bare lookup by id with NO includes.</summary>
    Task<Section?> FindByIdAsync(int id);

    /// <summary>All sections for a venue, ordered by <see cref="Section.SortOrder"/> then Id.</summary>
    Task<List<Section>> GetByVenueAsync(int venueId);

    /// <summary>
    /// All sections for a venue, ordered by <see cref="Section.SortOrder"/> then Id, with
    /// <see cref="Section.Resources"/> eager-loaded when <paramref name="includeResources"/> is true.
    /// Used by the admin resources grid.
    /// </summary>
    Task<List<Section>> GetByVenueAsync(int venueId, bool includeResources);

    /// <summary>Current count of sections in a venue — used to compute the next SortOrder on insert.</summary>
    Task<int> CountByVenueAsync(int venueId);

    /// <summary>
    /// Applies a new display order to a venue's sections.
    /// Returns null if the venue doesn't exist, false if <paramref name="sectionIds"/> doesn't
    /// exactly match the venue's current sections (count, distinctness, ids), true on success.
    /// </summary>
    Task<bool?> ReorderAsync(int venueId, IReadOnlyList<int> sectionIds);

    /// <summary>Adds a section.</summary>
    Task AddAsync(Section section);

    /// <summary>Removes a section.</summary>
    void Remove(Section section);

    /// <summary>Bare filtered lookup by (sectionId, venueId) with NO includes — used by update-section.</summary>
    Task<Section?> FindForVenueAsync(int sectionId, int venueId);

    /// <summary>Filtered lookup by (sectionId, venueId) with <see cref="Section.Resources"/> eager-loaded — used by delete-section to FK-null affected bookings.</summary>
    Task<Section?> GetWithResourcesForVenueAsync(int sectionId, int venueId);

    /// <summary>Flushes pending changes on the underlying DbContext.</summary>
    Task SaveChangesAsync();
}
