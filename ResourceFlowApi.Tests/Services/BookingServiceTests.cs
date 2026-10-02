using Microsoft.EntityFrameworkCore;
using Moq;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

// Partial so the group-hold cases can live in their own file (BookingServiceGroupHoldTests.cs)
// while still compiling as this type. The concrete repositories and HoldService that CreateService
// builds are internal and gated by [OnlyAccessibleBy]; sharing this type's identity keeps those
// tests inside the existing grant instead of widening it for a new class name.
public partial class BookingServiceTests
{
    private static BookingService CreateService(
        AppDbContext db,
        IHoldService? holdService = null,
        IBookingConfirmationService? confirmationService = null,
        INotificationQueue? notificationQueue = null,
        ICurrentUserService? currentUser = null)
    {
        // Auto-assign tests need a real in-memory HoldService so PlaceAutoHold actually places
        // holds (a loose Mock<IHoldService> returns null from PlaceAutoHold, which the service
        // interprets as "all candidates held"). Tests that don't exercise auto-assign can pass
        // their own mock.
        holdService ??= new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        return new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdService,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdService),
            new ResourceGroupRepository(db),
            confirmationService,
            notificationQueue,
            currentUser);
    }

    private sealed class UtcClock : ISystemClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// Seeds a venue with two sections and a spread of resource sizes, for auto-assign
    /// tests that need to assert "smallest fitting free resource across sections".
    ///
    /// Section 1 "Main":  T1 (capacity 2), T2 (capacity 4), T3 (capacity 6)
    /// Section 2 "Annex": P1 (capacity 2), P2 (capacity 4)
    /// </summary>
    private static void SeedMultiResourceVenue(AppDbContext db)
    {
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Auto-Assign Venue",
            OpenTime = "00:00",
            CloseTime = "23:59",
            Timezone = "UTC"
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Sections.Add(new Section { Id = 2, Name = "Annex", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 3, Name = "T3", Capacity = 6, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 4, Name = "P1", Capacity = 2, SectionId = 2 });
        db.Resources.Add(new Resource { Id = 5, Name = "P2", Capacity = 4, SectionId = 2 });
        db.SaveChanges();
    }

    // ── CreateBookingAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBookingAsync_ReturnsDto_WithCorrectFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_ReturnsDto_WithCorrectFields));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal("guest@example.com", result.CustomerEmail);
        Assert.Equal(2, result.PartySize);
        Assert.NotEmpty(result.BookingRef!);
    }

    [Fact]
    public async Task CreateBookingAsync_PersistsToDatabase()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_PersistsToDatabase));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        BookingDto result = await svc.CreateBookingAsync(dto);
        Booking? entity = await db.Bookings.FindAsync(result.Id);
        if (entity != null)
        {
            db.Entry(entity).State = EntityState.Detached;
        }

        Booking? inDb = await db.Bookings.FindAsync(result.Id);
        Assert.NotNull(inDb);
        Assert.Equal("guest@example.com", inDb.CustomerEmail);
    }

    [Fact]
    public async Task CreateBookingAsync_GeneratesUniqueBookingRefs()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GeneratesUniqueBookingRefs));
        TestSeed.BasicVenue(db);
        // Add a second resource so we can create two bookings on the same date
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var date = DateTime.UtcNow.AddDays(7);

        BookingDto a = await svc.CreateBookingAsync(new BookingDto
        { VenueId = 1, SectionId = 1, ResourceId = 1, CustomerEmail = "a@x.com", PartySize = 2, Date = date });
        BookingDto b = await svc.CreateBookingAsync(new BookingDto
        { VenueId = 1, SectionId = 1, ResourceId = 2, CustomerEmail = "b@x.com", PartySize = 2, Date = date });

        Assert.NotEqual(a.BookingRef, b.BookingRef);
    }

    [Fact]
    public async Task CreateBookingAsync_UsesNumericRef_WhenVenueIsConfiguredForIt()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_UsesNumericRef_WhenVenueIsConfiguredForIt));
        TestSeed.BasicVenue(db);
        Venue venue = await db.Venues.SingleAsync();
        venue.BookingRefFormat = BookingRefFormat.Numeric;
        await db.SaveChangesAsync();

        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        Assert.All(result.BookingRef!, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.Equal(NumericBookingRefGenerator.Digits, result.BookingRef!.Length);
    }

    [Fact]
    public async Task CreateBookingAsync_UsesWordRef_WhenVenueUsesTheDefaultFormat()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_UsesWordRef_WhenVenueUsesTheDefaultFormat));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(4, result.BookingRef!.Split('-').Length);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenResourceAlreadyBooked()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenResourceAlreadyBooked));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "first@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        await svc.CreateBookingAsync(dto);

        var dto2 = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "second@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto2));

        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenResourceHeldByOther()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenResourceHeldByOther));
        TestSeed.BasicVenue(db);

        var holdMock = new Mock<IHoldService>();
        holdMock
            .Setup(h => h.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>()))
            .Returns(true);

        BookingService svc = CreateService(db, holdMock.Object);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto));

        Assert.Contains("held by another user", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_ReleasesHold_AfterSuccess()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_ReleasesHold_AfterSuccess));
        TestSeed.BasicVenue(db);

        var holdMock = new Mock<IHoldService>();
        holdMock
            .Setup(h => h.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), "my-hold-id"))
            .Returns(false);

        BookingService svc = CreateService(db, holdMock.Object);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            HoldId = "my-hold-id",
            Date = DateTime.UtcNow.AddDays(7)
        };

        await svc.CreateBookingAsync(dto);

        holdMock.Verify(h => h.ReleaseHold("my-hold-id"), Times.Once);
    }

    // ── Configurable booking duration ───────────────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(480)]
    public async Task CreateBookingAsync_EndTime_UsesVenueConfiguredDuration(int durationMinutes)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(CreateBookingAsync_EndTime_UsesVenueConfiguredDuration)}_{durationMinutes}");
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = durationMinutes });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(7);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal(result.Date.AddMinutes(durationMinutes), result.EndTime);
    }

    [Fact]
    public async Task CreateBookingAsync_EndTime_UsesThePartysDurationRule()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_EndTime_UsesThePartysDurationRule));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = 60,
            DurationRulesJson = """[{"minPartySize":1,"minutes":60},{"minPartySize":5,"minutes":120}]""",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 6, SectionId = 1 });
        db.SaveChanges();

        BookingDto result = await CreateService(db).CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 5,
            Date = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(result.Date.AddMinutes(120), result.EndTime);
    }

    [Fact]
    public async Task CreateBookingAsync_EndTime_DefaultsToOneHour_WhenVenueDurationNotSet()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_EndTime_DefaultsToOneHour_WhenVenueDurationNotSet));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(7);
        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date
        });

        Assert.Equal(result.Date.AddHours(1), result.EndTime);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenNewBookingDurationOverlapsLaterBooking()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenNewBookingDurationOverlapsLaterBooking));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = 120 });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime firstStart = DateTime.UtcNow.AddDays(7);
        // Existing booking starts 100 minutes after the new one — outside a fixed 60-minute
        // window, but inside the venue's configured 120-minute occupancy window.
        db.Bookings.Add(new Booking
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = firstStart.AddMinutes(100),
            EndTime = firstStart.AddMinutes(100).AddMinutes(120),
            CustomerEmail = "later@x.com",
            PartySize = 2,
            BookingRef = "LATER1"
        });
        await db.SaveChangesAsync();

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = firstStart
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto));
        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenOverlapsLegacyBookingWithoutEndTime_UsingConfiguredDuration()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenOverlapsLegacyBookingWithoutEndTime_UsingConfiguredDuration));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = 90 });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime legacyStart = DateTime.UtcNow.AddDays(7);
        // Legacy booking with no EndTime at all (pre-migration data)
        db.Bookings.Add(new Booking
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = legacyStart,
            EndTime = null,
            CustomerEmail = "legacy@x.com",
            PartySize = 2,
            BookingRef = "LEGACY1"
        });
        await db.SaveChangesAsync();

        BookingService svc = CreateService(db);
        // 70 minutes after the legacy booking — outside the old fixed 60-minute fallback,
        // but inside the venue's configured 90-minute window.
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = legacyStart.AddMinutes(70)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto));
        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_StoresSpecialRequests()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_StoresSpecialRequests));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7),
            SpecialRequests = "step-free access"
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal("step-free access", result.SpecialRequests);
    }

    // ── GetBookingByIdAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBookingByIdAsync_ReturnsDto_WhenFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingByIdAsync_ReturnsDto_WhenFound));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        BookingDto? result = await svc.GetBookingByIdAsync(created.Id);

        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task GetBookingByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingByIdAsync_ReturnsNull_WhenNotFound));
        TestSeed.BasicVenue(db);

        BookingDto? result = await CreateService(db).GetBookingByIdAsync(999);

        Assert.Null(result);
    }

    // ── GetBookingByRefAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetBookingByRefAsync_ReturnsDto_WhenFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingByRefAsync_ReturnsDto_WhenFound));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        BookingDto? result = await svc.GetBookingByRefAsync(created.BookingRef!);

        Assert.NotNull(result);
        Assert.Equal(created.BookingRef, result!.BookingRef);
    }

    // References are minted lowercase and guests paste them out of a confirmation email, so a
    // capitalised or space-padded ref is the guest's keyboard, not a wrong reference.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GetBookingByRefAsync_ToleratesCaseAndSurroundingWhitespace(bool upper, bool padded)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(GetBookingByRefAsync_ToleratesCaseAndSurroundingWhitespace)}-{upper}-{padded}");
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        string typed = created.BookingRef!;
        if (upper)
        {
            typed = typed.ToUpperInvariant();
        }

        if (padded)
        {
            typed = $"  {typed} ";
        }

        BookingDto? result = await svc.GetBookingByRefAsync(typed);

        Assert.Equal(created.BookingRef, result?.BookingRef);
    }

    [Fact]
    public async Task GetBookingByRefAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingByRefAsync_ReturnsNull_WhenNotFound));
        TestSeed.BasicVenue(db);

        BookingDto? result = await CreateService(db).GetBookingByRefAsync("no-such-ref");

        Assert.Null(result);
    }

    // ── GetBookingsByVenueAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetBookingsByVenueAsync_ReturnsOnlyMatchingVenue()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingsByVenueAsync_ReturnsOnlyMatchingVenue));
        TestSeed.BasicVenue(db);
        // Second venue + resource
        db.Venues.Add(new Venue { Id = 2, Name = "Other Place" });
        db.Sections.Add(new Section { Id = 2, Name = "Main", VenueId = 2 });
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 2 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var date = DateTime.UtcNow.AddDays(7);
        await svc.CreateBookingAsync(new BookingDto
        { VenueId = 1, SectionId = 1, ResourceId = 1, CustomerEmail = "a@x.com", PartySize = 2, Date = date });
        await svc.CreateBookingAsync(new BookingDto
        { VenueId = 2, SectionId = 2, ResourceId = 2, CustomerEmail = "b@x.com", PartySize = 2, Date = date });

        var results = (await svc.GetBookingsByVenueAsync(1)).ToList();

        Assert.Single(results);
        Assert.Equal("a@x.com", results[0].CustomerEmail);
    }

    // ── DeleteBookingAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBookingAsync_RemovesFromDatabase()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteBookingAsync_RemovesFromDatabase));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        await svc.DeleteBookingAsync(created.Id);

        Assert.Null(await db.Bookings.FindAsync(created.Id));
    }

    // ── Capacity Validation ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenPartySizeExceedsResourceCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenPartySizeExceedsResourceCapacity));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 5,
            Date = DateTime.UtcNow.AddDays(7)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto));

        Assert.Contains("has capacity 4", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_Succeeds_WhenPartySizeEqualsResourceCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Succeeds_WhenPartySizeEqualsResourceCapacity));
        TestSeed.BasicVenue(db);

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 4,
            Date = DateTime.UtcNow.AddDays(7)
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal(4, result.PartySize);
        Assert.NotEmpty(result.BookingRef!);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenResourceExceedsConfiguredOversize()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_Throws_WhenResourceExceedsConfiguredOversize));
        TestSeed.BasicVenue(db); // Resource 1 has capacity 4.
        db.Venues.Single().MaxSpareCapacity = 1; // max 1 spare place
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2, // 4-place resource for a party of 2 → 2 spare places, over the cap of 1
            Date = DateTime.UtcNow.AddDays(7)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateBookingAsync(dto));

        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_AllowsResourceAtOversizeBoundary()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_AllowsResourceAtOversizeBoundary));
        TestSeed.BasicVenue(db); // Resource 1 has capacity 4.
        db.Venues.Single().MaxSpareCapacity = 1; // max 1 spare place
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 3, // 4-place resource for a party of 3 → 1 spare place, at the cap
            Date = DateTime.UtcNow.AddDays(7)
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal(3, result.PartySize);
    }

    [Fact]
    public async Task CreateBookingAsync_SkipsCapacityChecks_WhenResourceIdNoLongerExists()
    {
        // ResourceId/SectionId were explicitly supplied (skipping auto-assign), but the resource row
        // has since vanished (e.g. deleted between page load and submit) — GetByIdAsync returns
        // null, so both the lower-bound and oversize capacity guards must short-circuit false
        // rather than throwing, and the booking still lands with the caller-supplied ResourceId.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_SkipsCapacityChecks_WhenResourceIdNoLongerExists));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 999, // does not exist
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        BookingDto result = await svc.CreateBookingAsync(dto);

        Assert.Equal(999, result.ResourceId);
        Assert.NotEmpty(result.BookingRef!);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_FallsBackToCandidateSearch_WhenHeldResourceWasBookedElsewhere()
    {
        // The caller's hold points at a specific resource, but the DB says it's already booked
        // (e.g. an admin recorded a walk-in on it after the hold was placed) — ResolveAutoAssignAsync
        // must fall through past the "adopt the held resource" shortcut into a fresh candidate search
        // rather than persisting a doomed booking or throwing.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_FallsBackToCandidateSearch_WhenHeldResourceWasBookedElsewhere));
        SeedMultiResourceVenue(db);
        DateTime date = DateTime.UtcNow.AddDays(13);

        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 2, SectionId = 1, Date = date, BookingRef = "TAKEN" });
        db.SaveChanges();

        var holdMock = new Mock<IHoldService>();
        holdMock
            .Setup(h => h.GetHold("hold-T2"))
            .Returns(new HoldEntry("hold-T2", ResourceId: 2, SectionId: 1, VenueId: 1, Date: date, ExpiresAt: DateTime.UtcNow.AddMinutes(5)));
        holdMock.Setup(h => h.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>())).Returns(false);
        holdMock
            .Setup(h => h.PlaceAutoHold(1, It.IsAny<IReadOnlyList<ResourceCandidate>>(), date, "hold-T2", It.IsAny<int>()))
            .Returns(new AutoAssignResult("hold-fresh", DateTime.UtcNow.AddMinutes(5), 1, 1));
        holdMock.Setup(h => h.ReleaseHold(It.IsAny<string>()));

        BookingService svc = new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdMock.Object,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdMock.Object),
            new ResourceGroupRepository(db));

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date,
            HoldId = "hold-T2"
        });

        // The candidate search ran and assigned Resource 1 (fresh hold), not the already-booked Resource 2.
        Assert.Equal(1, result.ResourceId);
        holdMock.Verify(h => h.PlaceAutoHold(1, It.IsAny<IReadOnlyList<ResourceCandidate>>(), date, "hold-T2", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_FallsBackToCandidateSearch_WhenHoldIdIsStaleOrExpired()
    {
        // The caller sends a HoldId, but the in-memory hold store no longer has an entry for it
        // (it expired or was never valid) — GetHold returns null, so ResolveAutoAssignAsync must
        // skip the "adopt held resource" shortcut entirely and run the normal candidate search.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_FallsBackToCandidateSearch_WhenHoldIdIsStaleOrExpired));
        SeedMultiResourceVenue(db);
        DateTime date = DateTime.UtcNow.AddDays(13);

        var holdMock = new Mock<IHoldService>();
        holdMock.Setup(h => h.GetHold("stale-hold")).Returns((HoldEntry?)null);
        holdMock.Setup(h => h.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>())).Returns(false);
        holdMock
            .Setup(h => h.PlaceAutoHold(1, It.IsAny<IReadOnlyList<ResourceCandidate>>(), date, "stale-hold", It.IsAny<int>()))
            .Returns(new AutoAssignResult("hold-fresh", DateTime.UtcNow.AddMinutes(5), 1, 1));
        holdMock.Setup(h => h.ReleaseHold(It.IsAny<string>()));

        BookingService svc = new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdMock.Object,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdMock.Object),
            new ResourceGroupRepository(db));

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date,
            HoldId = "stale-hold"
        });

        Assert.Equal(1, result.ResourceId);
        holdMock.Verify(h => h.PlaceAutoHold(1, It.IsAny<IReadOnlyList<ResourceCandidate>>(), date, "stale-hold", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenBookingInPast()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenBookingInPast));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        var dto = new BookingDto { VenueId = 1, Date = DateTime.UtcNow.AddHours(-1) };
        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(dto));
    }

    [Fact]
    public async Task UpdateBookingAsync_SetsDefaultEndTime_WhenMissing()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_SetsDefaultEndTime_WhenMissing));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        var dto = new BookingDto { Id = created.Id, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2, EndTime = null };
        await svc.UpdateBookingAsync(created.Id, dto);
        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        Assert.Equal(date.AddHours(1), inDb.EndTime);
    }

    [Fact]
    public async Task UpdateBookingAsync_Throws_WhenResourceExceedsConfiguredOversize()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(UpdateBookingAsync_Throws_WhenResourceExceedsConfiguredOversize));
        TestSeed.BasicVenue(db); // Resource 1 has capacity 4.
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(
            new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 4 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        // Now cap spare capacity at 1 and shrink the party to 2 → the 4-place resource is too large.
        db.Venues.Single().MaxSpareCapacity = 1;
        db.SaveChanges();

        var dto = new BookingDto
        {
            Id = created.Id,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = date,
            PartySize = 2,
            EndTime = date.AddHours(1)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.UpdateBookingAsync(created.Id, dto));

        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task UpdateBookingAsync_Throws_WhenPartySizeExceedsResourceCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_Throws_WhenPartySizeExceedsResourceCapacity));
        TestSeed.BasicVenue(db); // Resource 1 has capacity 4.
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(
            new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        var dto = new BookingDto
        {
            Id = created.Id,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = date,
            PartySize = 99,
            EndTime = date.AddHours(1)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.UpdateBookingAsync(created.Id, dto));

        Assert.Contains("has capacity", ex.Message);
    }

    [Fact]
    public async Task UpdateBookingAsync_FixesInvalidEndTime()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_FixesInvalidEndTime));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        var dto = new BookingDto { Id = created.Id, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), PartySize = 2 };
        await svc.UpdateBookingAsync(created.Id, dto);
        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        Assert.Equal(date.AddHours(1), inDb.EndTime);
    }

    [Fact]
    public async Task UpdateBookingAsync_SetsDefaultEndTime_UsingVenueConfiguredDuration_WhenMissing()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_SetsDefaultEndTime_UsingVenueConfiguredDuration_WhenMissing));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = 90 });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        var dto = new BookingDto { Id = created.Id, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2, EndTime = null };
        await svc.UpdateBookingAsync(created.Id, dto);
        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        Assert.Equal(date.AddMinutes(90), inDb.EndTime);
    }

    [Fact]
    public async Task UpdateBookingAsync_FixesInvalidEndTime_UsingVenueConfiguredDuration()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_FixesInvalidEndTime_UsingVenueConfiguredDuration));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", DefaultBookingDurationMinutes = 90 });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        var dto = new BookingDto { Id = created.Id, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), PartySize = 2 };
        await svc.UpdateBookingAsync(created.Id, dto);
        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        Assert.Equal(date.AddMinutes(90), inDb.EndTime);
    }

    [Fact]
    public async Task UpdateBookingAsync_SkipsCapacityCheck_WhenNoResourceAssigned()
    {
        // booking.ResourceId is null (an unassigned/auto-assign-pending booking) — the ternary
        // that resolves `resource` must short-circuit to null without calling the resource repository,
        // and the capacity guard below it must be skipped entirely rather than throwing.
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_SkipsCapacityCheck_WhenNoResourceAssigned));
        TestSeed.BasicVenue(db);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = null, SectionId = null, Date = DateTime.UtcNow.AddHours(1), BookingRef = "B1" });
        db.SaveChanges();
        db.Entry((await db.Bookings.FindAsync(1))!).State = EntityState.Detached;

        BookingService svc = CreateService(db);
        var dto = new BookingDto { Id = 1, VenueId = 1, Date = DateTime.UtcNow.AddHours(1), PartySize = 99, EndTime = null };

        await svc.UpdateBookingAsync(1, dto);

        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == 1);
        Assert.Equal(99, inDb.PartySize);
        Assert.NotNull(inDb.EndTime);
    }

    [Fact]
    public async Task UpdateBookingAsync_UsesSixtyMinuteDefault_WhenVenueNoLongerExists()
    {
        // booking.VenueId points at a venue row that no longer exists (orphaned after
        // a venue deletion) — GetByIdAsync returns null, so both the oversize guard and the
        // EndTime default must fall back to the hardcoded 60-minute default instead of throwing
        // a NullReferenceException on `venue.DefaultBookingDurationMinutes`.
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_UsesSixtyMinuteDefault_WhenVenueNoLongerExists));
        TestSeed.BasicVenue(db); // seeds Resource 1 (capacity 4) under VenueId 1
        DateTime date = DateTime.UtcNow.AddHours(1);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 999, ResourceId = 1, SectionId = 1, Date = date, PartySize = 2, BookingRef = "B1" });
        db.SaveChanges();
        db.Entry((await db.Bookings.FindAsync(1))!).State = EntityState.Detached;

        BookingService svc = CreateService(db);
        var dto = new BookingDto { Id = 1, VenueId = 999, ResourceId = 1, SectionId = 1, Date = date, PartySize = 2, EndTime = null };

        await svc.UpdateBookingAsync(1, dto);

        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == 1);
        Assert.Equal(date.AddMinutes(60), inDb.EndTime);
    }

    [Fact]
    public async Task GetVenueNameAsync_ReturnsNull_WhenVenueNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetVenueNameAsync_ReturnsNull_WhenVenueNotFound));
        BookingService svc = CreateService(db);

        Assert.Null(await svc.GetVenueNameAsync(999));
    }

    [Fact]
    public async Task GetVenueNameAsync_ReturnsName_WhenVenueFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetVenueNameAsync_ReturnsName_WhenVenueFound));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);

        Assert.Equal("Test Venue", await svc.GetVenueNameAsync(1));
    }

    [Fact]
    public async Task UpdateBookingAsync_AlwaysRecomputesEndTime_EvenWhenCallerSuppliesAnAlreadyValidOne()
    {
        // BookingMapper.ToEntity ignores BookingDto.EndTime entirely (both as source and as a
        // Booking.EndTime target) for updates, so `booking.EndTime` is always null immediately
        // after mapping — the "!booking.EndTime.HasValue" guard is therefore always true and
        // EndTime is unconditionally recomputed from the venue's configured duration,
        // regardless of whatever (even a perfectly valid) EndTime the caller supplied.
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateBookingAsync_AlwaysRecomputesEndTime_EvenWhenCallerSuppliesAnAlreadyValidOne));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 });
        db.Entry((await db.Bookings.FindAsync(created.Id))!).State = EntityState.Detached;

        DateTime explicitValidEndTime = date.AddMinutes(45);
        var dto = new BookingDto { Id = created.Id, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2, EndTime = explicitValidEndTime };
        await svc.UpdateBookingAsync(created.Id, dto);

        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        // Recomputed from BasicVenue's default 60-minute duration, not the 45-minute value supplied.
        Assert.Equal(date.AddMinutes(60), inDb.EndTime);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsFalse_WhenBookingHasNoStoredEmail()
    {
        // booking.CustomerEmail is null (e.g. an admin-recorded walk-in) — the `?.Trim()`
        // null-conditional must short-circuit to null, which never string-equals a real
        // customer-supplied email, so the lookup correctly reports "not found" instead of
        // throwing a NullReferenceException.
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_ReturnsFalse_WhenBookingHasNoStoredEmail));
        TestSeed.BasicVenue(db);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = null, BookingRef = "B1" });
        db.SaveChanges();

        BookingService svc = CreateService(db);

        Assert.False(await svc.CancelBookingAsync("B1", "someone@test.com"));
    }

    [Fact]
    public async Task CancelBookingAsync_NotifiesQueue_WithVenueName_WhenVenueExists()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_NotifiesQueue_WithVenueName_WhenVenueExists));
        TestSeed.BasicVenue(db);
        BookingService svcForCreate = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svcForCreate.CreateBookingAsync(new BookingDto
        {
            VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2, CustomerEmail = "guest@example.com"
        });

        var notificationQueue = new Mock<INotificationQueue>();
        var holdSvc = new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        BookingService svc = new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdSvc,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdSvc),
            new ResourceGroupRepository(db),
            notificationQueue: notificationQueue.Object);

        bool result = await svc.CancelBookingAsync(created.BookingRef!, "guest@example.com");

        Assert.True(result);
        notificationQueue.Verify(n => n.EnqueueBookingCancelled(It.IsAny<Booking>(), "Test Venue"), Times.Once);
    }

    [Fact]
    public async Task CancelBookingAsync_UsesEmptyVenueName_WhenVenueIsArchived()
    {
        // VenueRepository.GetByIdAsync (used here purely to name the cancellation
        // notification) filters out archived venues, so a booking whose venue has
        // since been archived must fall back to "" instead of the queue call throwing on a
        // null Name. Note this is a different repository call than the one that loaded the
        // booking itself (which still Include()s the live Venue row regardless of archive
        // status), so the booking is found and cancelled normally.
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_UsesEmptyVenueName_WhenVenueIsArchived));
        TestSeed.BasicVenue(db);
        BookingService svcForCreate = CreateService(db);
        DateTime date = DateTime.UtcNow.AddHours(1);
        BookingDto created = await svcForCreate.CreateBookingAsync(new BookingDto
        {
            VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2, CustomerEmail = "guest@example.com"
        });

        db.Venues.Single().IsArchived = true;
        db.SaveChanges();

        var notificationQueue = new Mock<INotificationQueue>();
        var holdSvc = new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        BookingService svc = new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdSvc,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdSvc),
            new ResourceGroupRepository(db),
            notificationQueue: notificationQueue.Object);

        bool result = await svc.CancelBookingAsync(created.BookingRef!, "guest@example.com");

        Assert.True(result);
        notificationQueue.Verify(n => n.EnqueueBookingCancelled(It.IsAny<Booking>(), ""), Times.Once);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsFalse_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_ReturnsFalse_WhenNotFound));
        BookingService svc = CreateService(db);
        Assert.False(await svc.CancelBookingAsync("invalid", "test@test.com"));
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsFalse_WhenEmailMismatch()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_ReturnsFalse_WhenEmailMismatch));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = "real@test.com", PartySize = 2 });
        Assert.False(await svc.CancelBookingAsync(created.BookingRef!, "wrong@test.com"));
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsTrue_WhenAlreadyCancelled()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_ReturnsTrue_WhenAlreadyCancelled));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = "test@test.com", PartySize = 2 });
        await svc.CancelBookingAsync(created.BookingRef!, "test@test.com");
        Assert.True(await svc.CancelBookingAsync(created.BookingRef!, "test@test.com"));
    }

    [Fact]
    public async Task CancelBookingAsync_Throws_WhenBookingDateIsInThePast()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_Throws_WhenBookingDateIsInThePast));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = "test@test.com", PartySize = 2 });

        Booking booking = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        booking.Date = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.CancelBookingAsync(created.BookingRef!, "test@test.com"));

        Booking inDb = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        Assert.False(inDb.IsCancelled);
    }

    [Fact]
    public async Task CancelBookingAsync_Succeeds_WithinFiveMinuteGracePeriod()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_Succeeds_WithinFiveMinuteGracePeriod));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = "test@test.com", PartySize = 2 });

        Booking booking = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        booking.Date = DateTime.UtcNow.AddMinutes(-4);
        await db.SaveChangesAsync();

        Assert.True(await svc.CancelBookingAsync(created.BookingRef!, "test@test.com"));
    }

    [Fact]
    public async Task CancelBookingAsync_Throws_JustOutsideFiveMinuteGracePeriod()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CancelBookingAsync_Throws_JustOutsideFiveMinuteGracePeriod));
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);
        BookingDto created = await svc.CreateBookingAsync(new BookingDto { VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), CustomerEmail = "test@test.com", PartySize = 2 });

        Booking booking = await db.Bookings.FirstAsync(b => b.Id == created.Id);
        booking.Date = DateTime.UtcNow.AddMinutes(-6);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.CancelBookingAsync(created.BookingRef!, "test@test.com"));
    }

    // ── Booking confirmation delegation ────────────────────────────────────────
    // The full email pipeline (template rendering, SMTP send, failure logging) now lives in
    // BookingConfirmationService (see BookingConfirmationServiceTests + EmailTemplateServiceTests).
    // BookingService's only responsibility here is the delegation seam.

    [Fact]
    public async Task CreateBookingAsync_DelegatesToConfirmationService_WhenProvided()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_DelegatesToConfirmationService_WhenProvided));
        TestSeed.BasicVenue(db);
        var confirmationMock = new Mock<IBookingConfirmationService>();
        BookingService svc = CreateService(db, confirmationService: confirmationMock.Object);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1, SectionId = 1, ResourceId = 1,
            CustomerEmail = "guest@example.com", PartySize = 2,
            // Relative future date — the rest of this file uses AddDays(7); a hardcoded date rots
            // once it passes (these two tests failed once 2026-08-01 elapsed).
            Date = DateTime.UtcNow.AddDays(7),
        });

        Assert.NotEmpty(result.BookingRef!);
        // SendConfirmationAsync(Booking, Venue) called exactly once with the persisted booking.
        confirmationMock.Verify(
            c => c.SendConfirmationAsync(It.IsAny<Booking>(), It.IsAny<Venue>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_Succeeds_WhenNoConfirmationServiceInjected()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Succeeds_WhenNoConfirmationServiceInjected));
        TestSeed.BasicVenue(db);
        // No IBookingConfirmationService — booking must still succeed (no email sent).
        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1, SectionId = 1, ResourceId = 1,
            CustomerEmail = "guest@example.com", PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7),
        });

        Assert.NotEmpty(result.BookingRef!);
    }

    // ── Walk-in-only policy ───────────────────────────────────────────────────

    /// <summary>Next future occurrence of the given weekday, at 12:00 UTC.</summary>
    private static DateTime NextUtcOccurrence(DayOfWeek dayOfWeek)
    {
        DateTime d = DateTime.UtcNow.Date.AddDays(1);
        while (d.DayOfWeek != dayOfWeek)
        {
            d = d.AddDays(1);
        }

        return d.AddHours(12);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenLocationIsWalkInOnly()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenLocationIsWalkInOnly));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", WalkInOnly = true });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        };

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => svc.CreateBookingAsync(dto));
        Assert.Contains("walk-ins only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenDateFallsOnWalkInDay()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Throws_WhenDateFallsOnWalkInDay));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", Timezone = "UTC", WalkInDays = "6" });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        var dto = new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = NextUtcOccurrence(DayOfWeek.Saturday)
        };

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(dto));
    }

    [Fact]
    public async Task CreateBookingAsync_Succeeds_OnNonWalkInDay()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_Succeeds_OnNonWalkInDay));
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue", Timezone = "UTC", WalkInDays = "6" });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);
        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = NextUtcOccurrence(DayOfWeek.Wednesday)
        });

        Assert.NotEmpty(result.BookingRef!);
    }

    // ── CreateBookingAsync — booking pause ────────────────────────────────────
    //
    // A pause closes the slots that start inside its window, not booking as a whole. The
    // pair is what states that: a single "rejects while paused" test passes just as happily
    // against a service that refuses every booking until the pause expires.

    [Fact]
    public async Task CreateBookingAsync_RejectsBooking_InsideThePauseWindow()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_RejectsBooking_InsideThePauseWindow));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Paused Venue",
            Timezone = "UTC",
            BookingsPausedUntil = DateTime.UtcNow.AddHours(4)
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddHours(2)
        }));
    }

    [Fact]
    public async Task CreateBookingAsync_AllowsBooking_AfterThePauseWindow()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AllowsBooking_AfterThePauseWindow));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Paused Venue",
            Timezone = "UTC",
            BookingsPausedUntil = DateTime.UtcNow.AddHours(4)
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();

        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddHours(6)
        });

        Assert.NotEmpty(result.BookingRef!);
    }

    // ── CreateBookingAsync — auto-assign ("Any section") ──────────────────────
    //
    // When ResourceId/SectionId are both null, the service picks the smallest fitting free
    // resource across all sections, atomically places a hold, and persists the booking against
    // the resolved resource. These tests cover each branch of that path.

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_PicksSmallestFittingFreeResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_PicksSmallestFittingFreeResource));
        SeedMultiResourceVenue(db);
        BookingService svc = CreateService(db);

        // Party of 3 — fits T2(4)/T3(6)/P2(4) but not T1(2)/P1(2). Smallest fitting is T2.
        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 3,
            Date = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(2, result.ResourceId); // T2 — smallest fitting free
        Assert.Equal(1, result.SectionId);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_RespectsMaxResourceOversizeCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_AutoAssign_RespectsMaxResourceOversizeCapacity));
        SeedMultiResourceVenue(db); // T1(2), T2(4), T3(6), P1(2), P2(4)
        db.Venues.Single().MaxSpareCapacity = 1; // max 1 spare place
        db.SaveChanges();
        BookingService svc = CreateService(db);

        // Party of 2 — every resource fits, but a party of 2 may only take 1 spare place, so only
        // T1(2)/P1(2) qualify (0 spare). T2/T3/P2 are excluded by the oversize cap.
        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(7)
        });

        // Smallest fitting free resource among the qualifying set — T1 (id 1, tiebreak over P1).
        Assert.Equal(1, result.ResourceId);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_ThrowsWhenOversizeExcludesAllCandidates()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_AutoAssign_ThrowsWhenOversizeExcludesAllCandidates));
        SeedMultiResourceVenue(db); // T1(2), T2(4), T3(6), P1(2), P2(4)
        db.Venues.Single().MaxSpareCapacity = 0; // resources must fit the party exactly
        db.SaveChanges();
        BookingService svc = CreateService(db);

        // Party of 3 — no 3-place resource exists, and an exact-fit is required (0 spare). No
        // candidate qualifies, so auto-assign reports no availability rather than assigning
        // the party to an oversized resource.
        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 3,
            Date = DateTime.UtcNow.AddDays(7)
        }));
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_TiebreaksByResourceId()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_TiebreaksByResourceId));
        SeedMultiResourceVenue(db);
        BookingService svc = CreateService(db);

        // Party of 2 — both T1(2) and P1(2) fit. Tie-break by ResourceId: T1 (id 1) < P1 (id 4).
        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(8)
        });

        Assert.Equal(1, result.ResourceId);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_FallsThroughToNextResource_WhenFirstTaken()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_FallsThroughToNextResource_WhenFirstTaken));
        SeedMultiResourceVenue(db);
        // Pre-book T1 (the smallest 2-place resource) for the target date.
        DateTime date = DateTime.UtcNow.AddDays(9);
        db.Bookings.Add(new Booking
        {
            VenueId = 1, ResourceId = 1, SectionId = 1, Date = date,
            BookingRef = "PREBOOK", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date
        });

        // T1 is taken; next smallest 2-place resource is P1 (id 4).
        Assert.Equal(4, result.ResourceId);
        Assert.Equal(2, result.SectionId);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_CrossesSections_WhenSectionFull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_CrossesSections_WhenSectionFull));
        SeedMultiResourceVenue(db);
        DateTime date = DateTime.UtcNow.AddDays(10);
        // Fill every Main-section resource that fits a party of 2.
        foreach (int tid in new[] { 1, 2, 3 })
        {
            db.Bookings.Add(new Booking
            {
                VenueId = 1, ResourceId = tid, SectionId = 1, Date = date,
                BookingRef = $"PRE{tid}", EndTime = date.AddMinutes(60)
            });
        }
        db.SaveChanges();
        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date
        });

        // All Main resources booked → auto-assign should land in Annex (section 2).
        Assert.Equal(2, result.SectionId);
        Assert.Equal(4, result.ResourceId); // P1, smallest Annex 2-place resource
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_Throws_WhenNoEligibleResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_Throws_WhenNoEligibleResource));
        SeedMultiResourceVenue(db);
        BookingService svc = CreateService(db);

        // Party of 8 — no resource fits (largest in SeedMultiResourceVenue is 6).
        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 8,
            Date = DateTime.UtcNow.AddDays(11)
        }));

        Assert.Contains("No resources are available", ex.Message);
    }

    [Theory]
    [InlineData(null, 1)]   // ResourceId null, SectionId set
    [InlineData(1, null)]   // ResourceId set, SectionId null
    public async Task CreateBookingAsync_AutoAssign_Throws_WhenExactlyOneIdNull(int? resourceId, int? sectionId)
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_Throws_WhenExactlyOneIdNull) + $"_{resourceId}_{sectionId}");
        SeedMultiResourceVenue(db);
        BookingService svc = CreateService(db);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            ResourceId = resourceId,
            SectionId = sectionId,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(12)
        }));

        Assert.Contains("Specify both ResourceId and SectionId", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_AdoptsResourceFromValidHold()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_AdoptsResourceFromValidHold));
        SeedMultiResourceVenue(db);
        DateTime date = DateTime.UtcNow.AddDays(13);

        // Simulate a pre-existing auto-assigned hold on T2 via a mocked IHoldService.
        // The service's ResolveAutoAssignAsync should adopt the held resource/section without
        // running the candidate search.
        var holdMock = new Mock<IHoldService>();
        holdMock
            .Setup(h => h.GetHold("hold-T2"))
            .Returns(new HoldEntry("hold-T2", ResourceId: 2, SectionId: 1, VenueId: 1, Date: date, ExpiresAt: DateTime.UtcNow.AddMinutes(5)));
        holdMock.Setup(h => h.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>())).Returns(false);
        holdMock.Setup(h => h.ReleaseHold(It.IsAny<string>()));
        BookingService svc = new BookingService(
            new BookingRepository(db),
            new ResourceRepository(db),
            new SectionRepository(db),
            new VenueRepository(db),
            holdMock.Object,
            new BookingMapper(),
            new ResourceAutoAssigner(new BookingRepository(db), holdMock.Object),
            new ResourceGroupRepository(db));

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 4,
            Date = date,
            HoldId = "hold-T2"
        });

        Assert.Equal(2, result.ResourceId); // adopted from the hold
        Assert.Equal(1, result.SectionId);
        // The candidate path must not have been invoked.
        holdMock.Verify(h => h.PlaceAutoHold(It.IsAny<int>(), It.IsAny<IReadOnlyList<ResourceCandidate>>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_Throws_WhenAllCandidatesAreHeldByOthers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_Throws_WhenAllCandidatesAreHeldByOthers));
        TestSeed.BasicVenue(db); // single resource, capacity 4
        DateTime date = DateTime.UtcNow.AddDays(15);

        var holdService = new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        // Someone else already holds the only eligible resource for this slot.
        HoldResult? otherHold = holdService.PlaceHold(venueId: 1, resourceId: 1, sectionId: 1, bookingDate: date);
        Assert.NotNull(otherHold);

        BookingService svc = CreateService(db, holdService);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "guest@example.com",
            PartySize = 2,
            Date = date
        }));

        // The held resource is excluded at candidate-building (ResourceAutoAssigner checks IsResourceHeld),
        // so there are no eligible candidates — the "no resources available" message fires. The
        // "currently being held by other users" path now only triggers when candidates exist but
        // lose a placement-time race.
        Assert.Contains("No resources are available", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_PersistsResolvedResourceOnBookingRow()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_PersistsResolvedResourceOnBookingRow));
        SeedMultiResourceVenue(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(14);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "persist@example.com",
            PartySize = 2,
            Date = date
        });

        // Reload from DB to confirm the resolved resource/section were persisted.
        Booking? persisted = await db.Bookings.FirstOrDefaultAsync(b => b.BookingRef == result.BookingRef);
        Assert.NotNull(persisted);
        Assert.Equal(result.ResourceId, persisted!.ResourceId);
        Assert.Equal(result.SectionId, persisted.SectionId);
    }

    // ── Combinable resource group bookings ─────────────────────────────────────

    /// <summary>
    /// Seeds a venue with one 2-place standalone resource (T1) and a combinable group of two
    /// 4-place resources (T2, T3) with CombinedCapacity 8. Used for group-booking tests.
    /// </summary>
    private static void SeedVenueWithGroup(AppDbContext db)
    {
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Group Venue", OpenTime = "00:00", CloseTime = "23:59", Timezone = "UTC"
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 3, Name = "T3", Capacity = 4, SectionId = 1 });
        db.ResourceGroups.Add(new ResourceGroup
        {
            Id = 1, VenueId = 1, CombinedCapacity = 8,
            Members = new List<ResourceGroupMembership>
            {
                new() { ResourceGroupId = 1, ResourceId = 2 },
                new() { ResourceGroupId = 1, ResourceId = 3 }
            }
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_UsesNumericRef_WhenVenueIsConfiguredForIt()
    {
        // The group path mints its own reference in CreateGroupBookingAsync — a separate call
        // site from the single-resource path, so it needs its own guard against being left behind.
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateBookingAsync_GroupBooking_UsesNumericRef_WhenVenueIsConfiguredForIt));
        SeedVenueWithGroup(db);
        Venue venue = await db.Venues.SingleAsync();
        venue.BookingRefFormat = BookingRefFormat.Numeric;
        await db.SaveChangesAsync();

        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group-numeric@example.com",
            PartySize = 6,
            Date = DateTime.UtcNow.AddDays(21),
            ResourceGroupId = 1
        });

        Assert.Equal(1, result.ResourceGroupId);
        Assert.All(result.BookingRef!, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_PersistsResourceGroupId_WithNullResourceId()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_PersistsResourceGroupId_WithNullResourceId));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(20);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        });

        Assert.Equal(1, result.ResourceGroupId);
        Assert.Null(result.ResourceId);

        Booking? persisted = await db.Bookings.FirstOrDefaultAsync(b => b.BookingRef == result.BookingRef);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.ResourceGroupId);
        Assert.Null(persisted.ResourceId);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsWhenMemberIsBooked()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsWhenMemberIsBooked));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(21);
        // Book member T2 for an overlapping slot.
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 2, SectionId = 1, Date = date,
            BookingRef = "X1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        }));

        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsWhenMemberHeldByOther()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsWhenMemberHeldByOther));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(22);
        var holdService = new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        // Someone else holds member T3.
        Assert.NotNull(holdService.PlaceHold(venueId: 1, resourceId: 3, sectionId: 1, bookingDate: date));
        BookingService svc = CreateService(db, holdService);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        }));

        Assert.Contains("held by another user", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_IgnoresClientSuppliedMemberResourceIds()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_IgnoresClientSuppliedMemberResourceIds));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(23);
        var holdService = new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());
        Assert.NotNull(holdService.PlaceHold(venueId: 1, resourceId: 3, sectionId: 1, bookingDate: date));
        BookingService svc = CreateService(db, holdService);

        // MemberResourceIds is part of the public POST body: omitting the held member must not skip
        // the hold check for it — members always come from the persisted group.
        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "sneaky@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = Array.Empty<int>()
        }));

        Assert.Contains("held by another user", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsGroupLeftWithFewerThanTwoMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsGroupLeftWithFewerThanTwoMembers));
        SeedVenueWithGroup(db);
        // Simulate the DB cascade that fires when a member resource is deleted out from under a group.
        db.ResourceGroupMemberships.Remove(db.ResourceGroupMemberships.First(m => m.ResourceId == 3));
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 6,
            Date = DateTime.UtcNow.AddDays(24),
            ResourceGroupId = 1
        }));

        Assert.Contains("no longer be combined", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsOversizeCap()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsOversizeCap));
        SeedVenueWithGroup(db);
        var r = db.Venues.First();
        r.MaxSpareCapacity = 2; // party of 2 at 8-place group: 8 - 2 = 6 > 2
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 2,
            Date = DateTime.UtcNow.AddDays(23),
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        }));

        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_ThrowsNotFound_WhenGroupMissing()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_ThrowsNotFound_WhenGroupMissing));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group@example.com",
            PartySize = 6,
            Date = DateTime.UtcNow.AddDays(24),
            ResourceGroupId = 999, // nonexistent
            MemberResourceIds = new[] { 2, 3 }
        }));
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_ResolvesToGroup_WhenOnlyGroupsFit()
    {
        // No standalone resource fits a party of 6 (T1 has capacity 2, T2/T3 are grouped), so auto-assign
        // must resolve to the group and persist ResourceGroupId.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_ResolvesToGroup_WhenOnlyGroupsFit));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(25);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "auto-group@example.com",
            PartySize = 6,
            Date = date
            // No ResourceId/SectionId → auto-assign
        });

        Assert.Equal(1, result.ResourceGroupId);
        Assert.Null(result.ResourceId);

        Booking? persisted = await db.Bookings.FirstOrDefaultAsync(b => b.BookingRef == result.BookingRef);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.ResourceGroupId);
    }

    // ── Double-booking regressions (group-aware conflict checks) ────────────
    //
    // These cover the gap that a persisted group booking stores ResourceId = null, so the original
    // resource-only IsResourceBookedOnDateAsync could not see it and allowed the same physical resources to
    // be booked again. IsUnitBookedOnDateAsync resolves the membership and blocks all three cases.

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsWhenGroupAlreadyBooked()
    {
        // The same group is already booked for an overlapping slot — booking it again must conflict.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsWhenGroupAlreadyBooked));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(30);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceGroupId = 1, SectionId = 1, Date = date,
            BookingRef = "GRP1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group2@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        }));

        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_SingleResource_RejectsWhenItsGroupAlreadyBooked()
    {
        // The group (T2+T3) is booked; booking member T2 individually must conflict because the
        // group booking reserves T2 too (it persists ResourceId = null, invisible to a resource-only check).
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_SingleResource_RejectsWhenItsGroupAlreadyBooked));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(31);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceGroupId = 1, SectionId = 1, Date = date,
            BookingRef = "GRP1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "member@example.com",
            PartySize = 3,
            Date = date,
            ResourceId = 2,
            SectionId = 1
        }));

        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsWhenMemberReservedByAnotherGroup()
    {
        // A second group (G2) shares member T2 with G1. Booking G1 must conflict because T2 is
        // reserved by the G2 booking (which stores ResourceId = null). Seeds a second group manually.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsWhenMemberReservedByAnotherGroup));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(32);
        db.ResourceGroups.Add(new ResourceGroup
        {
            Id = 2, VenueId = 1, CombinedCapacity = 6,
            Members = new List<ResourceGroupMembership>
            {
                new() { ResourceGroupId = 2, ResourceId = 2 },
                new() { ResourceGroupId = 2, ResourceId = 1 }
            }
        });
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceGroupId = 2, SectionId = 1, Date = date,
            BookingRef = "GRP2", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "group1@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        }));

        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task GetBookingByIdAsync_GroupBooking_PopulatesGroupLabelAndCapacity()
    {
        // Display regression: a group booking must surface a readable resource label + combined capacity
        // on the read path (guest confirmation / lookup), not null fields that hide the resource row.
        using AppDbContext db = TestDbFactory.Create(nameof(GetBookingByIdAsync_GroupBooking_PopulatesGroupLabelAndCapacity));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);
        DateTime date = DateTime.UtcNow.AddDays(33);

        BookingDto created = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "display@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            MemberResourceIds = new[] { 2, 3 }
        });

        BookingDto? fetched = await svc.GetBookingByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(1, fetched!.ResourceGroupId);
        Assert.Equal(8, fetched.ResourceCapacity);
        Assert.Contains("T2", fetched.ResourceName);
        Assert.Contains("T3", fetched.ResourceName);
    }

    // ── Party-size validation (defense in depth behind the DTO [Range]) ──────────

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(BookingLimits.MaxPartySize + 1)]
    public async Task CreateBookingAsync_RejectsPartySizeOutOfRange(int partySize)
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_RejectsPartySizeOutOfRange) + partySize);
        TestSeed.BasicVenue(db);
        BookingService svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "x@example.com",
            PartySize = partySize,
            Date = DateTime.UtcNow.AddDays(40),
            ResourceId = 1,
            SectionId = 1
        }));
    }
}
