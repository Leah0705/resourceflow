using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

public interface IVenueRepository
{
    /// <summary>Eager-loads <see cref="Venue.Sections"/> with their <see cref="Section.Resources"/>. Excludes archived rows.</summary>
    Task<Venue?> GetByIdAsync(int id);

    // ── Bundle 2 additions ───────────────────────────────────────────────────

    /// <summary>Bare lookup by id with NO includes, NO archived filter — for pause/unpause/archive/delete where the soft-delete filter shouldn't apply.</summary>
    Task<Venue?> FindByIdAsync(int id);

    /// <summary>All non-archived venues, no navigation properties loaded.</summary>
    Task<List<Venue>> GetAllActiveAsync();

    /// <summary>All non-archived venues with <see cref="Venue.Sections"/> and their <see cref="Section.Resources"/> eager-loaded.</summary>
    Task<List<Venue>> GetAllActiveWithSectionsAsync();

    /// <summary>Persists a new venue and returns the saved entity with its assigned Id.</summary>
    Task<Venue> AddAsync(Venue venue);

    /// <summary>Removes a venue (caller should cascade-delete bookings first — see AdminService.DeleteVenueAsync).</summary>
    void Remove(Venue venue);

    /// <summary>Saves all pending changes on the underlying DbContext.</summary>
    Task SaveChangesAsync();

    /// <summary>True if any venue (archived or not) exists with this id.</summary>
    Task<bool> ExistsAsync(int id);

    /// <summary>
    /// Active (in-progress, non-cancelled) bookings count per non-archived venue, ordered by name.
    /// Each <see cref="LookupDto"/> carries the venue's Id/Name/BookingsPausedUntil/IsArchived plus the
    /// <see cref="LookupDto.ActiveBookingsCount"/> snapshot computed at <paramref name="nowUtc"/>.
    /// Centralizes the correlated-subquery projection previously inlined in AdminService.GetVenuesAsync.
    /// </summary>
    Task<List<LookupDto>> GetAllWithActiveBookingsCountAsync(DateTime nowUtc);

    /// <summary>
    /// Counts everything <c>AdminService.DeleteVenueAsync</c> would cascade away — sections,
    /// resources, resource groups and bookings (total plus the not-yet-started ones). Null when no
    /// venue has this id. Archived rows are included: the preview exists to be read before
    /// deleting one.
    /// </summary>
    Task<VenueDeletePreviewDto?> GetDeletePreviewAsync(int id, DateTime nowUtc);
}
