using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AvailabilityServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.AdminServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.HoldPolicyServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.MediaServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.VenueManagementServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.WalkInTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerRestoreTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerSectionsReorderTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerUpdateTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerEmailTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Controllers.AdminControllerLookupTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Integration.RepositoryTests")]
[ExternalAccessAllowed]
internal class VenueRepository(AppDbContext db) : IVenueRepository
{
    private readonly AppDbContext _db = db;

    public async Task<Venue?> GetByIdAsync(int id)
    {
        return await _db.Venues
            .Include(r => r.Sections)
            .ThenInclude(s => s.Resources)
            .Include(r => r.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.Resource)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsArchived);
    }

    // ── Bundle 2 additions ─────────────────────────────────────────────────────

    public async Task<Venue?> FindByIdAsync(int id)
    {
        return await _db.Venues.FindAsync(id);
    }

    public async Task<List<Venue>> GetAllActiveAsync()
    {
        return await _db.Venues.Where(r => !r.IsArchived).ToListAsync();
    }

    public async Task<List<Venue>> GetAllActiveWithSectionsAsync()
    {
        return await _db.Venues
            .Where(r => !r.IsArchived)
            .Include(r => r.Sections)
                .ThenInclude(s => s.Resources)
            .Include(r => r.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.Resource)
            .ToListAsync();
    }

    public async Task<Venue> AddAsync(Venue venue)
    {
        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();
        return venue;
    }

    public void Remove(Venue venue)
    {
        _db.Venues.Remove(venue);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _db.Venues.AnyAsync(r => r.Id == id);
    }

    public async Task<List<LookupDto>> GetAllWithActiveBookingsCountAsync(DateTime nowUtc)
    {
        return await _db.Venues
            .OrderBy(r => r.Name)
            .Select(r => new LookupDto
            {
                Id = r.Id,
                Name = r.Name,
                BookingsPausedUntil = r.BookingsPausedUntil,
                IsArchived = r.IsArchived,
                ActiveBookingsCount = _db.Bookings.Count(b =>
                    b.VenueId == r.Id &&
                    !b.IsCancelled &&
                    b.Date <= nowUtc &&
                    (b.EndTime.HasValue ? b.EndTime.Value > nowUtc : b.Date.AddMinutes(r.DefaultBookingDurationMinutes) > nowUtc)),
                UpcomingBookingsCount = _db.Bookings.Count(b =>
                    b.VenueId == r.Id && !b.IsCancelled && b.Date > nowUtc)
            })
            .ToListAsync();
    }

    public async Task<VenueDeletePreviewDto?> GetDeletePreviewAsync(int id, DateTime nowUtc)
    {
        return await _db.Venues
            .Where(r => r.Id == id)
            .Select(r => new VenueDeletePreviewDto
            {
                Id = r.Id,
                Name = r.Name,
                IsArchived = r.IsArchived,
                SectionCount = _db.Sections.Count(s => s.VenueId == r.Id),
                ResourceCount = _db.Resources.Count(t => t.Section!.VenueId == r.Id),
                ResourceGroupCount = _db.ResourceGroups.Count(g => g.VenueId == r.Id),
                BookingCount = _db.Bookings.Count(b => b.VenueId == r.Id),
                UpcomingBookingCount = _db.Bookings.Count(b =>
                    b.VenueId == r.Id && !b.IsCancelled && b.Date > nowUtc)
            })
            .FirstOrDefaultAsync();
    }
}
