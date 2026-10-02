using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Services;

public partial class BookingServiceTests
{
    // ── Walk-in-only resources ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateBookingAsync_RejectsAWalkInOnlyResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_RejectsAWalkInOnlyResource));
        SeedVenueWithGroup(db);
        db.Resources.Find(1)!.WalkInOnly = true;
        db.SaveChanges();

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db).CreateBookingAsync(new BookingDto
        {
            VenueId = 1, SectionId = 1, ResourceId = 1, CustomerEmail = "guest@example.com",
            PartySize = 2, Date = DateTime.UtcNow.AddDays(3),
        }));

        Assert.Equal(ErrorCodes.ResourceWalkInOnly, ex.Code);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task CreateBookingAsync_RejectsAGroupHoldingAWalkInOnlyResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_RejectsAGroupHoldingAWalkInOnlyResource));
        SeedVenueWithGroup(db);
        db.Resources.Find(3)!.WalkInOnly = true;
        db.SaveChanges();

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db).CreateBookingAsync(new BookingDto
        {
            VenueId = 1, ResourceGroupId = 1, CustomerEmail = "guest@example.com",
            PartySize = 6, Date = DateTime.UtcNow.AddDays(3),
        }));

        Assert.Equal(ErrorCodes.ResourceWalkInOnly, ex.Code);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssignsAroundAWalkInOnlyResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssignsAroundAWalkInOnlyResource));
        SeedVenueWithGroup(db);
        db.Resources.Find(1)!.WalkInOnly = true;
        db.SaveChanges();

        // T1 is the only 2-place resource, so a party of 2 would land there. Held back, it gets a 4-place one.
        BookingDto result = await CreateService(db).CreateBookingAsync(new BookingDto
        {
            VenueId = 1, CustomerEmail = "guest@example.com", PartySize = 2, Date = DateTime.UtcNow.AddDays(3),
        });

        Assert.NotEqual(1, result.ResourceId);
    }

    // ── Guest pacing ────────────────────────────────────────────────────────

    /// <summary>
    /// Four 4-place resources open all day on 30-minute slots, capped at six guests per slot, with a party of
    /// four already starting at 19:15 in the 19:00 slot. Returns 19:00 two days out.
    /// </summary>
    private static DateTime SeedPacedVenue(AppDbContext db)
    {
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Paced", OpenTime = "00:00", CloseTime = "23:59", Timezone = "UTC",
            BookingSlotIntervalMinutes = 30, MaxGuestsPerSlot = 6,
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        for (int id = 1; id <= 4; id++)
        {
            db.Resources.Add(new Resource { Id = id, Name = $"T{id}", Capacity = 4, SectionId = 1 });
        }

        DateTime slot = DateTime.UtcNow.Date.AddDays(2).AddHours(19);
        db.Bookings.Add(new Booking
        {
            VenueId = 1, SectionId = 1, ResourceId = 1, PartySize = 4, BookingRef = "PACE1",
            Date = slot.AddMinutes(15), EndTime = slot.AddMinutes(75),
        });
        db.SaveChanges();
        return slot;
    }

    private static BookingDto PartyAt(DateTime date, int partySize, int resourceId) => new()
    {
        VenueId = 1, SectionId = 1, ResourceId = resourceId, CustomerEmail = "guest@example.com",
        PartySize = partySize, Date = date,
    };

    [Fact]
    public async Task CreateBookingAsync_ConcurrentRequestsForOneResource_OnlyOneSucceeds()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_ConcurrentRequestsForOneResource_OnlyOneSucceeds));
        TestSeed.VenueWithHours(db);
        DateTime slot = DateTime.UtcNow.Date.AddDays(2).AddHours(11);
        BookingService first = CreateService(db);
        BookingService second = CreateService(db);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateAsync(BookingService service)
        {
            await start.Task;
            try
            {
                await service.CreateBookingAsync(PartyAt(slot, partySize: 2, resourceId: 1));
                return true;
            }
            catch (ConflictException ex) when (ex.Code == ErrorCodes.BookingResourceConflict)
            {
                return false;
            }
        }

        Task<bool> firstRequest = Task.Run(() => TryCreateAsync(first));
        Task<bool> secondRequest = Task.Run(() => TryCreateAsync(second));
        start.SetResult();
        bool[] results = await Task.WhenAll(firstRequest, secondRequest);
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task CreateBookingAsync_ConcurrentRequestsForDifferentResources_RespectGuestCap()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_ConcurrentRequestsForDifferentResources_RespectGuestCap));
        DateTime slot = SeedPacedVenue(db);
        BookingService first = CreateService(db);
        BookingService second = CreateService(db);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateAsync(BookingService service, int resourceId)
        {
            await start.Task;
            try
            {
                await service.CreateBookingAsync(PartyAt(slot, partySize: 2, resourceId));
                return true;
            }
            catch (ConflictException ex) when (ex.Code == ErrorCodes.BookingPacingFull)
            {
                return false;
            }
        }

        Task<bool> firstRequest = Task.Run(() => TryCreateAsync(first, 2));
        Task<bool> secondRequest = Task.Run(() => TryCreateAsync(second, 3));
        start.SetResult();
        bool[] results = await Task.WhenAll(firstRequest, secondRequest);
        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(6, db.Bookings.Sum(b => b.PartySize));
    }

    [Fact]
    public async Task CreateBookingAsync_AcceptsAPartyThatExactlyFillsTheGuestCap()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AcceptsAPartyThatExactlyFillsTheGuestCap));
        DateTime slot = SeedPacedVenue(db);

        await CreateService(db).CreateBookingAsync(PartyAt(slot, partySize: 2, resourceId: 2));

        Assert.Equal(6, db.Bookings.Sum(b => b.PartySize));
    }

    [Fact]
    public async Task CreateBookingAsync_RejectsAPartyOneGuestOverTheGuestCap()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_RejectsAPartyOneGuestOverTheGuestCap));
        DateTime slot = SeedPacedVenue(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateService(db).CreateBookingAsync(PartyAt(slot, partySize: 3, resourceId: 2)));

        Assert.Equal(ErrorCodes.BookingPacingFull, ex.Code);
        Assert.Equal(6, ex.Args!["cap"]);
        Assert.Equal(2, ex.Args["remaining"]);
        Assert.Equal("19:00", ex.Args["time"]);
    }

    [Fact]
    public async Task CreateBookingAsync_CountsAnOffGridTimeAgainstItsSlot()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_CountsAnOffGridTimeAgainstItsSlot));
        DateTime slot = SeedPacedVenue(db);

        // 19:10 is not a slot the page offers, but it is still inside the 19:00 slot.
        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateService(db).CreateBookingAsync(PartyAt(slot.AddMinutes(10), partySize: 3, resourceId: 2)));

        Assert.Equal(ErrorCodes.BookingPacingFull, ex.Code);
    }

    [Fact]
    public async Task CreateBookingAsync_IgnoresGuestsStartingInThePreviousSlot()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_IgnoresGuestsStartingInThePreviousSlot));
        DateTime slot = SeedPacedVenue(db);

        // The 19:15 party has already started by 19:30, so the whole cap is free again.
        await CreateService(db).CreateBookingAsync(PartyAt(slot.AddMinutes(30), partySize: 4, resourceId: 2));
        await CreateService(db).CreateBookingAsync(PartyAt(slot.AddMinutes(45), partySize: 2, resourceId: 3));

        Assert.Equal(3, db.Bookings.Count());
    }

    [Fact]
    public async Task CreateBookingAsync_IgnoresCancelledGuests()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_IgnoresCancelledGuests));
        DateTime slot = SeedPacedVenue(db);
        db.Bookings.Single().IsCancelled = true;
        db.SaveChanges();

        await CreateService(db).CreateBookingAsync(PartyAt(slot, partySize: 4, resourceId: 2));

        Assert.Equal(2, db.Bookings.Count());
    }
}
