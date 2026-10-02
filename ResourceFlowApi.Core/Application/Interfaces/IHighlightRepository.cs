using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>Persistence of <see cref="VenueHighlight"/> rows (the marketing bullets shown on the homepage).</summary>
public interface IHighlightRepository
{
    /// <summary>All highlights ordered by SortOrder then Id.</summary>
    Task<List<VenueHighlight>> GetAllAsync();

    /// <summary>Looks up a single highlight by id (no navigation properties).</summary>
    Task<VenueHighlight?> FindByIdAsync(int id);

    /// <summary>Adds and saves a highlight, returning the persisted entity.</summary>
    Task<VenueHighlight> AddAsync(VenueHighlight highlight);

    /// <summary>Flushes pending changes on a tracked entity.</summary>
    Task SaveChangesAsync();

    /// <summary>Removes a highlight (caller has already loaded it).</summary>
    void Remove(VenueHighlight highlight);
}
