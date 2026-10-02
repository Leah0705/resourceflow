using Microsoft.EntityFrameworkCore;
using Moq;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// Hold-adoption branches of <c>BookingService.ResolveAutoAssignAsync</c> — in particular adopting
/// an existing combinable-group hold instead of re-running the candidate search — and the
/// group-capacity guards in <c>CreateGroupBookingAsync</c>.
///
/// Declared as a partial of <see cref="BookingServiceTests"/> rather than its own class so it
/// reuses that type's <c>CreateService</c>/<c>SeedVenueWithGroup</c> helpers and, with them,
/// the <c>[OnlyAccessibleBy]</c> grant those helpers need to construct the internal repositories
/// and <c>HoldService</c>.
/// </summary>
public partial class BookingServiceTests
{
    /// <summary>
    /// A real in-memory hold service, so <c>PlaceGroupHold</c>/<c>PlaceAutoHold</c> actually place
    /// holds — a loose mock returns null, which the service reads as "everything is held".
    /// Constructed through <see cref="CreateService"/>'s own default wherever possible; this
    /// overload exists for the tests that need to place a hold before the booking call.
    /// </summary>
    private static IHoldService NewHoldService() =>
        new ResourceFlowApi.Infrastructure.Holds.HoldService(new UtcClock());

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_AdoptsGroupFromValidGroupHold()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_AdoptsGroupFromValidGroupHold));
        SeedVenueWithGroup(db);
        IHoldService holdService = NewHoldService();
        BookingService svc = CreateService(db, holdService);
        DateTime date = DateTime.UtcNow.AddDays(12);

        HoldResult? hold = holdService.PlaceGroupHold(
            venueId: 1, resourceGroupId: 1, memberResourceIds: [2, 3], sectionId: 1, bookingDate: date);
        Assert.NotNull(hold);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "adopted-group@example.com",
            PartySize = 6,
            Date = date,
            HoldId = hold!.HoldId
            // No ResourceId/SectionId/ResourceGroupId → auto-assign adopts the existing group hold.
        });

        Assert.Equal(1, result.ResourceGroupId);
        Assert.Null(result.ResourceId);
        Assert.Equal(1, result.SectionId);

        Booking? persisted = await db.Bookings.FirstOrDefaultAsync(b => b.BookingRef == result.BookingRef);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.ResourceGroupId);
        Assert.Null(persisted.ResourceId);

        // The hold is released once the booking lands.
        Assert.Null(holdService.GetHold(hold.HoldId));
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_IgnoresGroupHold_WhenTheGroupWasBookedElsewhere()
    {
        // The held group got booked by someone else between hold and submit. The stale hold must be
        // discarded and the candidate search re-run — here nothing else fits a party of 6, so it conflicts.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_IgnoresGroupHold_WhenTheGroupWasBookedElsewhere));
        SeedVenueWithGroup(db);
        DateTime date = DateTime.UtcNow.AddDays(13);
        db.Bookings.Add(new Booking
        {
            Id = 99, VenueId = 1, ResourceGroupId = 1, SectionId = 1, Date = date,
            BookingRef = "TAKEN", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();

        IHoldService holdService = NewHoldService();
        BookingService svc = CreateService(db, holdService);
        HoldResult? hold = holdService.PlaceGroupHold(1, 1, [2, 3], 1, date);
        Assert.NotNull(hold);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "stale-group@example.com",
            PartySize = 6,
            Date = date,
            HoldId = hold!.HoldId
        }));

        Assert.Contains("No resources are available", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBookingAsync_AutoAssign_IgnoresHoldBelongingToAnotherVenue()
    {
        // A hold placed against a different venue must not be adopted — otherwise a caller could
        // reuse a foreign hold id to skip this venue's own availability checks.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_AutoAssign_IgnoresHoldBelongingToAnotherVenue));
        SeedVenueWithGroup(db);
        IHoldService holdService = NewHoldService();
        BookingService svc = CreateService(db, holdService);
        DateTime date = DateTime.UtcNow.AddDays(14);

        HoldResult? foreignHold = holdService.PlaceHold(
            venueId: 999, resourceId: 2, sectionId: 1, bookingDate: date);
        Assert.NotNull(foreignHold);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "foreign-hold@example.com",
            PartySize = 2,
            Date = date,
            HoldId = foreignHold!.HoldId
        });

        // Resolved by a fresh candidate search rather than by adopting the foreign hold: T1 (capacity 2)
        // is the smallest fitting resource, and T2 is still held by the other venue's hold.
        Assert.Equal(1, result.ResourceId);
        Assert.Equal(1, result.SectionId);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_RejectsPartyLargerThanCombinedCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_RejectsPartyLargerThanCombinedCapacity));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "too-big@example.com",
            PartySize = 9,
            Date = DateTime.UtcNow.AddDays(15),
            ResourceGroupId = 1
        }));

        Assert.Equal("This group has a combined capacity of 8, but 9 guests were requested.", ex.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_DerivesSectionFromLowestMember_WhenNotSupplied()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_DerivesSectionFromLowestMember_WhenNotSupplied));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "no-section@example.com",
            PartySize = 6,
            Date = DateTime.UtcNow.AddDays(16),
            ResourceGroupId = 1
            // SectionId omitted — the service resolves it from the lowest-numbered member resource.
        });

        Assert.Equal(1, result.SectionId);
        Booking? persisted = await db.Bookings.FirstOrDefaultAsync(b => b.BookingRef == result.BookingRef);
        Assert.Equal(1, persisted!.SectionId);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_NotifiesAdminsAndEmailsTheGuest()
    {
        // The single-resource path enqueues both notifications and sends the confirmation; the group
        // path is a separate method and must not quietly skip either.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_NotifiesAdminsAndEmailsTheGuest));
        SeedVenueWithGroup(db);
        var queue = new Mock<INotificationQueue>();
        var confirmations = new Mock<IBookingConfirmationService>();
        BookingService svc = CreateService(db, null, confirmations.Object, queue.Object);
        DateTime date = DateTime.UtcNow.AddDays(18);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "notified@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1
        });

        Assert.Equal(1, result.ResourceGroupId);
        queue.Verify(q => q.EnqueueBookingCreated(
            It.Is<Booking>(b => b.ResourceGroupId == 1 && b.ResourceId == null), "Group Venue"), Times.Once);
        queue.Verify(q => q.EnqueueCapacityCheck(1, "Group Venue", It.IsAny<DateTime>()), Times.Once);
        confirmations.Verify(c => c.SendConfirmationAsync(
            It.Is<Booking>(b => b.ResourceGroupId == 1),
            It.Is<Venue>(r => r.Id == 1)), Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_SucceedsWithoutANotificationQueueOrConfirmationService()
    {
        // Both collaborators are optional constructor args; a group booking must still persist when
        // neither is wired up (the shape the unit tests and the seeder use).
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_SucceedsWithoutANotificationQueueOrConfirmationService));
        SeedVenueWithGroup(db);
        BookingService svc = CreateService(db);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "quiet@example.com",
            PartySize = 6,
            Date = DateTime.UtcNow.AddDays(19),
            ResourceGroupId = 1
        });

        Assert.Equal(1, result.ResourceGroupId);
    }

    [Fact]
    public async Task CreateBookingAsync_GroupBooking_SucceedsWhenTheCallersOwnHoldCoversTheMembers()
    {
        // The submitter's own group hold must be excluded from the "held by another user" check,
        // otherwise the hold they were told to send would block their own booking.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateBookingAsync_GroupBooking_SucceedsWhenTheCallersOwnHoldCoversTheMembers));
        SeedVenueWithGroup(db);
        IHoldService holdService = NewHoldService();
        BookingService svc = CreateService(db, holdService);
        DateTime date = DateTime.UtcNow.AddDays(17);

        HoldResult? hold = holdService.PlaceGroupHold(1, 1, [2, 3], 1, date);
        Assert.NotNull(hold);

        BookingDto result = await svc.CreateBookingAsync(new BookingDto
        {
            VenueId = 1,
            CustomerEmail = "own-hold@example.com",
            PartySize = 6,
            Date = date,
            ResourceGroupId = 1,
            SectionId = 1,
            HoldId = hold!.HoldId
        });

        Assert.Equal(1, result.ResourceGroupId);
        Assert.Null(holdService.GetHold(hold.HoldId));
    }
}
