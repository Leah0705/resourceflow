using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Integration;

public class RepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static (Venue venue, Section section, Resource resource) SeedVenueData(AppDbContext db)
    {
        var venue = new Venue
        {
            Name = "Test Venue",
            Address = "1 Test Rd",
            Sections = new List<Section>
            {
                new Section
                {
                    Name = "Main Hall",
                    Resources = new List<Resource>
                    {
                        new Resource { Name = "A1", Capacity = 4 },
                        new Resource { Name = "A2", Capacity = 2 }
                    }
                }
            }
        };
        db.Venues.Add(venue);
        db.SaveChanges();

        Section section = venue.Sections.First();
        Resource resource = section.Resources.First();
        return (venue, section, resource);
    }

    // ─── BookingRepository ───────────────────────────────────────────

    [Fact]
    public async Task BookingRepository_AddAsync_CreatesBooking()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        var booking = new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(1),
            CustomerEmail = "test@test.com",
            PartySize = 2,
            BookingRef = "REF001"
        };

        Booking result = await repo.AddAsync(booking);

        Assert.True(result.Id > 0);
        Assert.Equal("REF001", result.BookingRef);
    }

    [Fact]
    public async Task BookingRepository_GetByIdAsync_ReturnsBookingWithIncludes()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        var booking = new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(2),
            CustomerEmail = "includes@test.com",
            PartySize = 3,
            BookingRef = "REF002"
        };
        await repo.AddAsync(booking);

        // Detach all entities so that GetByIdAsync has to load from DB with includes
        db.ChangeTracker.Clear();

        Booking? result = await repo.GetByIdAsync(booking.Id);

        Assert.NotNull(result);
        Assert.Equal("REF002", result!.BookingRef);
        Assert.NotNull(result.Resource);
        Assert.Equal("A1", result.Resource.Name);
        Assert.NotNull(result.Section);
        Assert.Equal("Main Hall", result.Section.Name);
        Assert.NotNull(result.Venue);
        Assert.Equal("Test Venue", result.Venue.Name);
    }

    [Fact]
    public async Task BookingRepository_GetByRefAsync_ReturnsCorrectBooking()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        var booking = new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(3),
            CustomerEmail = "ref@test.com",
            PartySize = 2,
            BookingRef = "UNIQUE-REF"
        };
        await repo.AddAsync(booking);
        db.ChangeTracker.Clear();

        Booking? result = await repo.GetByRefAsync("UNIQUE-REF");

        Assert.NotNull(result);
        Assert.Equal("ref@test.com", result!.CustomerEmail);
    }

    [Fact]
    public async Task BookingRepository_GetByRefAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = CreateContext();
        SeedVenueData(db);
        var repo = new BookingRepository(db);

        Booking? result = await repo.GetByRefAsync("NONEXISTENT");

        Assert.Null(result);
    }

    [Fact]
    public async Task BookingRepository_GetBookingsByVenueIdAsync_ReturnsOnlyMatchingBookings()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        Resource resource2 = section.Resources.Last();

        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(4),
            CustomerEmail = "a@test.com",
            PartySize = 2,
            BookingRef = "RESTA1"
        });
        await repo.AddAsync(new Booking
        {
            ResourceId = resource2.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(5),
            CustomerEmail = "b@test.com",
            PartySize = 2,
            BookingRef = "RESTA2"
        });

        var results = (await repo.GetBookingsByVenueIdAsync(venue.Id)).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, b => Assert.Equal(venue.Id, b.VenueId));
    }

    [Fact]
    public async Task BookingRepository_DeleteAsync_RemovesBooking()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        var booking = new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = DateTime.UtcNow.AddDays(6),
            CustomerEmail = "delete@test.com",
            PartySize = 1,
            BookingRef = "DEL001"
        };
        await repo.AddAsync(booking);

        await repo.DeleteAsync(booking.Id);

        Booking? result = await repo.GetByIdAsync(booking.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task BookingRepository_IsResourceBookedOnDateAsync_ReturnsTrue_WhenOverlap()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(7).AddHours(12).ToUniversalTime();

        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = bookingDate,
            EndTime = bookingDate.AddHours(1),
            CustomerEmail = "booked@test.com",
            PartySize = 2,
            BookingRef = "OVERLAP1"
        });

        // Check at a time that overlaps (30 minutes into the existing booking)
        bool isBooked = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate.AddMinutes(30));

        Assert.True(isBooked);
    }

    [Fact]
    public async Task BookingRepository_IsResourceBookedOnDateAsync_ReturnsFalse_WhenNoOverlap()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(8).AddHours(12).ToUniversalTime();

        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = bookingDate,
            EndTime = bookingDate.AddHours(1),
            CustomerEmail = "nooverlap@test.com",
            PartySize = 2,
            BookingRef = "NOOVERLAP1"
        });

        // Check at a time well after the existing booking ends
        bool isBooked = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate.AddHours(2));

        Assert.False(isBooked);
    }

    [Fact]
    public async Task BookingRepository_IsResourceBookedOnDateAsync_ReturnsFalse_WhenCancelled()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(9).AddHours(12).ToUniversalTime();

        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = bookingDate,
            EndTime = bookingDate.AddHours(1),
            CustomerEmail = "cancelled@test.com",
            PartySize = 2,
            BookingRef = "CANCELLED1",
            IsCancelled = true,
            CancelledAt = DateTime.UtcNow
        });

        // Same time as the cancelled booking — should be available
        bool isBooked = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate);

        Assert.False(isBooked);
    }

    // ── Configurable booking duration ───────────────────────────────────────

    [Fact]
    public async Task BookingRepository_IsResourceBookedOnDateAsync_UsesCustomDuration_ForLegacyBookingWithoutEndTime()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(10).AddHours(12).ToUniversalTime();

        // Legacy booking with no EndTime at all
        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = bookingDate,
            EndTime = null,
            CustomerEmail = "legacy@test.com",
            PartySize = 2,
            BookingRef = "LEGACY1"
        });

        // 75 minutes after the legacy booking's start: outside the old fixed 60-minute
        // window, but still inside a 90-minute configured duration.
        bool isBookedWithDefaultDuration = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate.AddMinutes(75));
        bool isBookedWithNinetyMinuteDuration = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate.AddMinutes(75), durationMinutes: 90);

        Assert.False(isBookedWithDefaultDuration);
        Assert.True(isBookedWithNinetyMinuteDuration);
    }

    [Fact]
    public async Task BookingRepository_IsResourceBookedOnDateAsync_ShorterDuration_AllowsAdjacentBooking()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(11).AddHours(12).ToUniversalTime();

        // Legacy booking with no EndTime, 30-minute configured duration
        await repo.AddAsync(new Booking
        {
            ResourceId = resource.Id,
            SectionId = section.Id,
            VenueId = venue.Id,
            Date = bookingDate,
            EndTime = null,
            CustomerEmail = "short@test.com",
            PartySize = 2,
            BookingRef = "SHORT1"
        });

        // 45 minutes after start: outside a 30-minute window (over-rejected by the old
        // fixed 60-minute assumption, correctly allowed with a 30-minute duration).
        bool isBooked = await repo.IsResourceBookedOnDateAsync(resource.Id, bookingDate.AddMinutes(45), durationMinutes: 30);

        Assert.False(isBooked);
    }

    [Fact]
    public async Task BookingRepository_AddRangeAsync_AddsAllBookingsToChangeTracker()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section? section, Resource? resource) = SeedVenueData(db);
        var repo = new BookingRepository(db);

        DateTime bookingDate = DateTime.UtcNow.Date.AddDays(12).AddHours(12).ToUniversalTime();
        var bookings = new List<Booking>
        {
            new Booking
            {
                ResourceId = resource.Id,
                SectionId = section.Id,
                VenueId = venue.Id,
                Date = bookingDate,
                CustomerEmail = "range1@test.com",
                PartySize = 2,
                BookingRef = "RANGE1"
            },
            new Booking
            {
                ResourceId = resource.Id,
                SectionId = section.Id,
                VenueId = venue.Id,
                Date = bookingDate.AddHours(2),
                CustomerEmail = "range2@test.com",
                PartySize = 2,
                BookingRef = "RANGE2"
            }
        };

        await repo.AddRangeAsync(bookings);
        await repo.SaveChangesAsync();

        Booking? first = await repo.GetByRefAsync("RANGE1");
        Booking? second = await repo.GetByRefAsync("RANGE2");
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    // ─── VenueRepository ────────────────────────────────────────

    [Fact]
    public async Task VenueRepository_ExistsAsync_ReturnsTrue_WhenVenueExists()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section _, Resource _) = SeedVenueData(db);
        var repo = new VenueRepository(db);

        bool exists = await repo.ExistsAsync(venue.Id);

        Assert.True(exists);
    }

    [Fact]
    public async Task VenueRepository_ExistsAsync_ReturnsFalse_WhenVenueMissing()
    {
        using AppDbContext db = CreateContext();
        var repo = new VenueRepository(db);

        bool exists = await repo.ExistsAsync(9999);

        Assert.False(exists);
    }

    [Fact]
    public async Task VenueRepository_GetByIdAsync_LoadsSectionsAndResources()
    {
        using AppDbContext db = CreateContext();
        (Venue? venue, Section _, Resource _) = SeedVenueData(db);
        db.ChangeTracker.Clear();
        var repo = new VenueRepository(db);

        Venue? result = await repo.GetByIdAsync(venue.Id);

        Assert.NotNull(result);
        Assert.Equal("Test Venue", result!.Name);
        Assert.Single(result.Sections);

        Section section = result.Sections.First();
        Assert.Equal("Main Hall", section.Name);
        Assert.Equal(2, section.Resources.Count);
    }

    [Fact]
    public async Task VenueRepository_GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = CreateContext();
        var repo = new VenueRepository(db);

        Venue? result = await repo.GetByIdAsync(9999);

        Assert.Null(result);
    }

    // ─── SectionRepository ───────────────────────────────────────────

    [Fact]
    public async Task SectionRepository_GetByIdAsync_ReturnsSection()
    {
        using AppDbContext db = CreateContext();
        (Venue _, Section? section, Resource _) = SeedVenueData(db);
        db.ChangeTracker.Clear();
        var repo = new SectionRepository(db);

        Section? result = await repo.GetByIdAsync(section.Id);

        Assert.NotNull(result);
        Assert.Equal("Main Hall", result!.Name);
    }

    [Fact]
    public async Task SectionRepository_GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = CreateContext();
        var repo = new SectionRepository(db);

        Section? result = await repo.GetByIdAsync(9999);

        Assert.Null(result);
    }

    [Fact]
    public async Task SectionRepository_FindByIdAsync_ReturnsSectionWithoutNavigationProperties()
    {
        using AppDbContext db = CreateContext();
        (Venue _, Section? section, Resource _) = SeedVenueData(db);
        db.ChangeTracker.Clear();
        var repo = new SectionRepository(db);

        Section? result = await repo.FindByIdAsync(section.Id);

        Assert.NotNull(result);
        Assert.Equal("Main Hall", result!.Name);
    }

    [Fact]
    public async Task SectionRepository_FindByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = CreateContext();
        var repo = new SectionRepository(db);

        Section? result = await repo.FindByIdAsync(9999);

        Assert.Null(result);
    }

    // ─── ResourceRepository ─────────────────────────────────────────────

    [Fact]
    public async Task ResourceRepository_GetByIdAsync_ReturnsResource()
    {
        using AppDbContext db = CreateContext();
        (Venue _, Section _, Resource? resource) = SeedVenueData(db);
        db.ChangeTracker.Clear();
        var repo = new ResourceRepository(db);

        Resource? result = await repo.GetByIdAsync(resource.Id);

        Assert.NotNull(result);
        Assert.Equal("A1", result!.Name);
        Assert.Equal(4, result.Capacity);
    }

    [Fact]
    public async Task ResourceRepository_GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = CreateContext();
        var repo = new ResourceRepository(db);

        Resource? result = await repo.GetByIdAsync(9999);

        Assert.Null(result);
    }
}
