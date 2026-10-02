using Microsoft.EntityFrameworkCore;
using Moq;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

public class AvailabilityServiceTests
{
    [Fact]
    public async Task GetAvailabilityAsync_ReturnsAllSlots_WhenNoBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsAllSlots_WhenNoBookings));
        TestSeed.VenueWithHours(db);
        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var holdSvc = new Mock<IHoldService>();
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        // 11:00 to 13:00 with 30 min slots = 4 slots
        Assert.Equal(4, result.Slots.Count);
        Assert.All(result.Slots, s => Assert.True(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_FiltersOccupiedSlots()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_FiltersOccupiedSlots));
        TestSeed.VenueWithHours(db);

        // Book both resources at 12:00
        var date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = date, BookingRef = "B1" });
        db.Bookings.Add(new Booking { Id = 2, VenueId = 1, ResourceId = 2, SectionId = 1, Date = date, BookingRef = "B2" });
        db.SaveChanges();

        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var holdSvc = new Mock<IHoldService>();
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        // Slot at 12:00 should be unavailable
        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.False(slot1200.IsAvailable);

        // Slots at 11:00 should be available (assuming 1 hour duration, 12:00 starts right when 11:00 ends)
        // Wait, 11:00 ends at 12:00. Booking is at 12:00. So 11:00 is fine.
        TimeSlotDto slot1100 = result.Slots.First(s => s.Time == "11:00");
        Assert.True(slot1100.IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesVenueConfiguredDuration_ForConflictWindow()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesVenueConfiguredDuration_ForConflictWindow));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            DefaultBookingDurationMinutes = 90
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        // Booking at 12:00 with no explicit EndTime -> should fall back to the
        // venue's configured 90-minute duration, occupying 12:00-13:30.
        var bookingStart = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = bookingStart, BookingRef = "B1" });
        db.SaveChanges();

        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var holdSvc = new Mock<IHoldService>();
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, bookingStart, 2);

        // With a 90-minute duration, the 11:00 slot (11:00-12:30) should now conflict
        // with the 12:00 booking, whereas with the old fixed 1-hour assumption it would not.
        TimeSlotDto slot1100 = result.Slots.First(s => s.Time == "11:00");
        Assert.False(slot1100.IsAvailable);

        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.False(slot1200.IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_RefusesALargePartyAGapThatOnlyFitsTheSmallerDurationRule()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_RefusesALargePartyAGapThatOnlyFitsTheSmallerDurationRule));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "14:00", Timezone = "UTC",
            DurationRulesJson = """[{"minPartySize":1,"minutes":60},{"minPartySize":5,"minutes":120}]""",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 6, SectionId = 1 });
        var nextSlot = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, PartySize = 2,
            Date = nextSlot, EndTime = nextSlot.AddMinutes(60), BookingRef = "B1",
        });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);
        var day = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        AvailabilityResponseDto twoTop = await svc.GetAvailabilityAsync(1, day, 2);
        AvailabilityResponseDto fiveTop = await svc.GetAvailabilityAsync(1, day, 5);

        Assert.True(twoTop.Slots.First(s => s.Time == "12:00").IsAvailable);
        Assert.False(fiveTop.Slots.First(s => s.Time == "12:00").IsAvailable);
        Assert.True(fiveTop.Slots.First(s => s.Time == "11:00").IsAvailable);
    }

    // ── BookingSlotIntervalMinutes ───────────────────────────────────────────
    // The interval controls the step between selectable start times, decoupled from the
    // booking duration. A 15-min interval must produce slots at every quarter hour, and
    // combining a longer duration with a shorter interval must not produce double-bookings.

    [Fact]
    public async Task GetAvailabilityAsync_UsesVenueConfiguredSlotInterval_ForStep()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesVenueConfiguredSlotInterval_ForStep));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "12:00", Timezone = "UTC",
            BookingSlotIntervalMinutes = 15
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        // 11:00 → 12:00 stepping by 15 min = 4 slots (11:00, 11:15, 11:30, 11:45).
        Assert.Equal(4, result.Slots.Count);
        Assert.Equal(["11:00", "11:15", "11:30", "11:45"], result.Slots.Select(s => s.Time).ToList());
    }

    [Fact]
    public async Task GetAvailabilityAsync_DefaultInterval_StepsEvery30Minutes()
    {
        // Backwards-compat: with no interval set, the entity default (30) preserves the
        // pre-setting slot grid exactly.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_DefaultInterval_StepsEvery30Minutes));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "12:00", Timezone = "UTC"
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.Equal(["11:00", "11:30"], result.Slots.Select(s => s.Time).ToList());
    }

    [Fact]
    public async Task GetAvailabilityAsync_60MinuteInterval_ProducesHourlySlots()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_60MinuteInterval_ProducesHourlySlots));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "14:00", Timezone = "UTC",
            BookingSlotIntervalMinutes = 60
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.Equal(["11:00", "12:00", "13:00"], result.Slots.Select(s => s.Time).ToList());
    }

    [Fact]
    public async Task GetAvailabilityAsync_LongDurationShortInterval_NoDoubleBooking()
    {
        // Core acceptance criterion: a 90-minute duration with a 15-minute interval.
        // A single resource booked at 12:00 must mark every overlapping generated start time
        // (11:15–12:15 all overlap a 12:00–13:30 booking window) unavailable — no start
        // time that would double-book the resource should be offered as available.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_LongDurationShortInterval_NoDoubleBooking));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "14:00", Timezone = "UTC",
            DefaultBookingDurationMinutes = 90,
            BookingSlotIntervalMinutes = 15
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        // Booking occupies 12:00–13:30 (90-min duration).
        var bookingStart = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = bookingStart, BookingRef = "B1" });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, bookingStart, 2);

        // Generated start times: 11:00, 11:15, 11:30, ..., 13:45. Each 90-min slot overlaps
        // the 12:00–13:30 booking window iff slotStart < 13:30 AND slotStart+90 > 12:00,
        // i.e. slotStart > 10:30. So every slot from 11:00 through 13:15 must be unavailable,
        // and 13:30 / 13:45 (which end at/after the booking, no overlap) must be available.
        var unavailable = result.Slots.Where(s => !s.IsAvailable).Select(s => s.Time).ToList();
        Assert.Contains("11:00", unavailable);
        Assert.Contains("11:15", unavailable);
        Assert.Contains("12:00", unavailable);
        Assert.Contains("13:15", unavailable);

        var available = result.Slots.Where(s => s.IsAvailable).Select(s => s.Time).ToList();
        Assert.Contains("13:30", available);
        Assert.Contains("13:45", available);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ConsidersHolds()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ConsidersHolds));
        TestSeed.VenueWithHours(db);

        var date = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);
        var holdSvc = new Mock<IHoldService>();
        // Hold both resources at 11:00
        holdSvc.Setup(h => h.IsResourceHeld(1, date, null)).Returns(true);
        holdSvc.Setup(h => h.IsResourceHeld(2, date, null)).Returns(true);

        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        TimeSlotDto slot1100 = result.Slots.First(s => s.Time == "11:00");
        Assert.False(slot1100.IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_FiltersByCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_FiltersByCapacity));
        TestSeed.VenueWithHours(db); // Resource 1 (capacity 2), Resource 2 (capacity 4)

        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var holdSvc = new Mock<IHoldService>();
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        // Request a party of 5
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 5);
        Assert.All(result.Slots, s => Assert.False(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_Throws_WhenVenueNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_Throws_WhenVenueNotFound));
        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var svc = new AvailabilityService(bookingRepo, restRepo, new Mock<IHoldService>().Object);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAvailabilityAsync(999, DateTime.UtcNow, 2));
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesUtc_WhenTimezoneInvalid()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesUtc_WhenTimezoneInvalid));
        db.Venues.Add(new Venue { Id = 1, Name = "T", Timezone = "Invalid/Timezone", OpenTime = "09:00", CloseTime = "10:00" });
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(result.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNoSlots_WhenPaused()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsNoSlots_WhenPaused));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "T", OpenTime = "09:00", CloseTime = "10:00", Timezone = "UTC",
            // Long enough that every one of tomorrow's slots sits inside the pause window
            // whatever time of day the suite runs.
            BookingsPausedUntil = DateTime.UtcNow.AddDays(2)
        });
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var result = await svc.GetAvailabilityAsync(1, DateTime.UtcNow.Date.AddDays(1), 2);
        Assert.NotEmpty(result.Slots);
        Assert.All(result.Slots, s => Assert.False(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_KeepsSlotsAfterThePauseWindow_Available()
    {
        // A pause is "stop accepting new arrivals for the next X hours", so it must not
        // close a slot that starts after the window ends.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_KeepsSlotsAfterThePauseWindow_Available));
        var section = new Section { Id = 1, Name = "S", Resources = new List<Resource> { new() { Id = 1, Name = "T1", Capacity = 4 } } };
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "T", OpenTime = "09:00", CloseTime = "22:00", Timezone = "UTC",
            BookingsPausedUntil = DateTime.UtcNow.AddHours(1),
            Sections = new List<Section> { section }
        });
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var result = await svc.GetAvailabilityAsync(1, DateTime.UtcNow.Date.AddDays(3), 2);

        Assert.All(result.Slots, s => Assert.True(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesDefaultHours_WhenTimeFormatInvalid()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesDefaultHours_WhenTimeFormatInvalid));
        db.Venues.Add(new Venue { Id = 1, Name = "T", OpenTime = "invalid", CloseTime = "", Timezone = "UTC" });
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2);
        // Default is 09:00 to 22:00 -> 13 hours * 2 slots/hour = 26 slots
        Assert.Equal(26, result.Slots.Count);
    }

    [Fact]
    public async Task GetAvailabilityAsync_HandlesDayNames_InOpenDays()
    {
        // This test ensures frontend format "Mon,Tue,Wed..." works correctly
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_HandlesDayNames_InOpenDays));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "T",
            OpenTime = "09:00",
            CloseTime = "22:00",
            Timezone = "UTC",
            OpenDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun"  // Frontend format
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Monday Jan 5, 2026
        var monday = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var result = await svc.GetAvailabilityAsync(1, monday, 2);

        Assert.NotEmpty(result.Slots);
        Assert.All(result.Slots, s => Assert.True(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNoSlots_WhenClosedDay()
    {
        // Weekend-only venue
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsNoSlots_WhenClosedDay));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "T",
            OpenTime = "09:00",
            CloseTime = "22:00",
            Timezone = "UTC",
            OpenDays = "Sat,Sun"  // Weekend only
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Monday Jan 5, 2026 - should have no slots
        var monday = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var result = await svc.GetAvailabilityAsync(1, monday, 2);

        Assert.Empty(result.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_PopulatesAvailableResourceIds()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_PopulatesAvailableResourceIds));
        TestSeed.VenueWithHours(db); // Resource 1 (capacity 2), Resource 2 (capacity 4)

        // Book Resource 1 at 12:00
        var date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = date, BookingRef = "B1" });
        db.SaveChanges();

        var bookingRepo = new BookingRepository(db);
        var restRepo = new VenueRepository(db);
        var holdSvc = new Mock<IHoldService>();
        var svc = new AvailabilityService(bookingRepo, restRepo, holdSvc.Object);

        // Case 1: Request a party of 2. At 12:00, only Resource 2 should be available.
        var result2PartySize = await svc.GetAvailabilityAsync(1, date, 2);
        var slot1200_2 = result2PartySize.Slots.First(s => s.Time == "12:00");
        Assert.Single(slot1200_2.AvailableResourceIds);
        Assert.Equal(2, slot1200_2.AvailableResourceIds[0]);

        // Case 2: Request a party of 4. At 12:00, Resource 2 should be available. Resource 1 is too small.
        var result4PartySize = await svc.GetAvailabilityAsync(1, date, 4);
        var slot1200_4 = result4PartySize.Slots.First(s => s.Time == "12:00");
        Assert.Single(slot1200_4.AvailableResourceIds);
        Assert.Equal(2, slot1200_4.AvailableResourceIds[0]);

        // Case 3: At 11:00, both resources should be available for a party of 2.
        var slot1100 = result2PartySize.Slots.First(s => s.Time == "11:00");
        Assert.Equal(2, slot1100.AvailableResourceIds.Count);
        Assert.Contains(1, slot1100.AvailableResourceIds);
        Assert.Contains(2, slot1100.AvailableResourceIds);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ExcludesOversizedResources_WhenMaxResourceOversizeCapacitySet()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(GetAvailabilityAsync_ExcludesOversizedResources_WhenMaxResourceOversizeCapacitySet));
        // VenueWithHours: Resource 1 (capacity 2), Resource 2 (capacity 4). Cap spare capacity at 1,
        // so a party of 2 may use Resource 1 (0 spare) or Resource 2 (2 spare) — Resource 2 is excluded.
        TestSeed.VenueWithHours(db);
        db.Venues.Single().MaxSpareCapacity = 1;
        db.SaveChanges();

        var svc = new AvailabilityService(
            new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        // The oversized 4-place Resource 2 must never appear in any slot's available ids.
        Assert.All(result.Slots, s => Assert.DoesNotContain(2, s.AvailableResourceIds));
        // The well-sized 2-place Resource 1 still appears on an open slot.
        Assert.Contains(result.Slots, s => s.AvailableResourceIds.Contains(1));
    }

    [Fact]
    public async Task GetAvailabilityAsync_IncludesOversizedResources_WhenMaxResourceOversizeCapacityUnset()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(GetAvailabilityAsync_IncludesOversizedResources_WhenMaxResourceOversizeCapacityUnset));
        TestSeed.VenueWithHours(db); // Resource 1 (capacity 2), Resource 2 (capacity 4), cap unset.

        var svc = new AvailabilityService(
            new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        // No cap → both resources are eligible for a party of 2 on an open slot.
        Assert.Contains(result.Slots, s => s.AvailableResourceIds.Contains(1) && s.AvailableResourceIds.Contains(2));
    }

    [Fact]
    public async Task GetAvailabilityAsync_CategorizesEverySlotByLocalHalfDay()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_CategorizesEverySlotByLocalHalfDay));
        TestSeed.VenueWithHours(db);

        var svc = new AvailabilityService(
            new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);
        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, date, 2);

        Assert.Equal("AM", result.Slots.Single(s => s.Time == "11:30").Category);
        Assert.Equal("PM", result.Slots.Single(s => s.Time == "12:00").Category);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsSlots_ForMondayInNegativeUtcOffsetTimezone()
    {
        // Regression test: midnight UTC on a Monday is Sunday evening in UTC-negative timezones.
        // The service must treat the incoming date as the local date (not convert from UTC).
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsSlots_ForMondayInNegativeUtcOffsetTimezone));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "T",
            OpenTime = "09:00",
            CloseTime = "22:00",
            Timezone = "America/New_York",  // UTC-4 in summer
            OpenDays = "1,2,3,4,5"         // Mon–Fri only
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Frontend sends the local date string "2026-06-01" (Monday), which ASP.NET Core
        // model-binds as DateTimeKind.Unspecified. Simulate that here.
        var monday = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var result = await svc.GetAvailabilityAsync(1, monday, 2);

        // Should return slots — the venue is open on Mondays
        Assert.NotEmpty(result.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesPerDayHours_ForOverriddenDay()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesPerDayHours_ForOverriddenDay));
        TestSeed.VenueWithHours(db);
        Venue r = db.Venues.First();
        // Saturday opens later and shorter than the uniform 11:00–13:00
        r.OpenHoursJson = """{"6":{"open":"12:00","close":"13:00"}}""";
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // 2026-10-10 is a Saturday (ISO day 6)
        var saturday = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, saturday, 2);

        // 12:00 to 13:00 with 30-min slots = 2 slots instead of the uniform 4
        Assert.Equal(2, result.Slots.Count);
        Assert.Equal("12:00", result.Slots[0].Time);
        Assert.Equal("12:30", result.Slots[1].Time);
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesUniformHours_ForDayWithoutOverride()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_UsesUniformHours_ForDayWithoutOverride));
        TestSeed.VenueWithHours(db);
        Venue r = db.Venues.First();
        r.OpenHoursJson = """{"6":{"open":"12:00","close":"13:00"}}""";
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // 2026-10-09 is a Friday (ISO day 5) — no override, uniform 11:00–13:00
        var friday = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, friday, 2);

        Assert.Equal(4, result.Slots.Count);
        Assert.Equal("11:00", result.Slots[0].Time);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ClosedDay_ReturnsNoSlots_EvenWithPerDayHours()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ClosedDay_ReturnsNoSlots_EvenWithPerDayHours));
        TestSeed.VenueWithHours(db);
        Venue r = db.Venues.First();
        // Saturday has hours configured but is excluded from OpenDays
        r.OpenDays = "1,2,3,4,5";
        r.OpenHoursJson = """{"6":{"open":"12:00","close":"13:00"}}""";
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        var saturday = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, saturday, 2);

        Assert.Empty(result.Slots);
    }

    // ── Walk-in-only policy ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNoSlots_WhenLocationIsWalkInOnly()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsNoSlots_WhenLocationIsWalkInOnly));
        TestSeed.VenueWithHours(db);
        Venue walkInOnly = db.Venues.First();
        walkInOnly.WalkInOnly = true;
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.Empty(result.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNoSlots_OnWalkInDay()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsNoSlots_OnWalkInDay));
        TestSeed.VenueWithHours(db);
        Venue withWalkInDay = db.Venues.First();
        withWalkInDay.WalkInDays = "6";
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // 2026-10-10 is a Saturday (ISO day 6) — walk-in only, no slots.
        AvailabilityResponseDto saturday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.Empty(saturday.Slots);

        // 2026-10-11 is a Sunday — bookings still allowed, 4 half-hour slots.
        AvailabilityResponseDto sunday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.Equal(4, sunday.Slots.Count);
    }

    [Fact]
    public async Task GetAvailabilityAsync_WrapsPastMidnight_WhenCloseTimeIsBeforeOpenTime()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_WrapsPastMidnight_WhenCloseTimeIsBeforeOpenTime));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Late Lounge", OpenTime = "22:00", CloseTime = "02:00", Timezone = "UTC",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        // 22:00 -> next day 02:00 in 30-min steps = 8 slots, wrapping past midnight.
        Assert.Equal(8, result.Slots.Count);
        Assert.Equal("22:00", result.Slots.First().Time);
        Assert.Equal("01:30", result.Slots.Last().Time);
        Assert.Contains(result.Slots, s => s.Time == "00:00");
    }

    [Fact]
    public async Task GetAvailabilityAsync_ParsesOpenDays_GivenSundayAsTextRatherThanIsoNumber()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ParsesOpenDays_GivenSundayAsTextRatherThanIsoNumber));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            OpenDays = "sunday",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // 2026-10-11 is a Sunday — "sunday" should parse to ISO day 7 and be treated as open.
        AvailabilityResponseDto sunday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(sunday.Slots);

        // 2026-10-10 is a Saturday — not in OpenDays, so no slots.
        AvailabilityResponseDto saturday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.Empty(saturday.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_TreatsEveryDayAsOpen_WhenOpenDaysHasNoRecognizableEntries()
    {
        // If every OpenDays entry fails to parse, openDaysList ends up empty after the `d > 0`
        // filter — the `openDaysList.Count > 0 && ...` guard short-circuits false in that case,
        // so the day-of-week check is skipped entirely rather than wrongly closing every day.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_TreatsEveryDayAsOpen_WhenOpenDaysHasNoRecognizableEntries));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            OpenDays = "notaday,alsobogus",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto monday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(monday.Slots);

        AvailabilityResponseDto saturday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(saturday.Slots);
    }

    [Theory]
    [InlineData("tue", 2026, 10, 13)]
    [InlineData("tues", 2026, 10, 13)]
    [InlineData("tuesday", 2026, 10, 13)]
    [InlineData("wed", 2026, 10, 14)]
    [InlineData("wednesday", 2026, 10, 14)]
    [InlineData("thu", 2026, 10, 15)]
    [InlineData("thurs", 2026, 10, 15)]
    [InlineData("thursday", 2026, 10, 15)]
    [InlineData("fri", 2026, 10, 16)]
    [InlineData("friday", 2026, 10, 16)]
    [InlineData("sat", 2026, 10, 17)]
    [InlineData("saturday", 2026, 10, 17)]
    public async Task GetAvailabilityAsync_ParsesOpenDays_GivenEachAbbreviatedDayName(string abbreviation, int year, int month, int day)
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(GetAvailabilityAsync_ParsesOpenDays_GivenEachAbbreviatedDayName) + "_" + abbreviation);
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            OpenDays = abbreviation,
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto matchingDay = await svc.GetAvailabilityAsync(
            1, new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(matchingDay.Slots);

        // The day before is not in OpenDays, so it must report no slots.
        AvailabilityResponseDto dayBefore = await svc.GetAvailabilityAsync(
            1, new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1), 2);
        Assert.Empty(dayBefore.Slots);
    }

    [Fact]
    public async Task GetAvailabilityAsync_FallsBackTo30MinuteInterval_WhenConfiguredIntervalIsZeroOrNegative()
    {
        // Defends against stale/corrupt rows where BookingSlotIntervalMinutes somehow ended up
        // 0 or negative — using it verbatim would spin the slot-generation while-loop forever.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_FallsBackTo30MinuteInterval_WhenConfiguredIntervalIsZeroOrNegative));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "12:00", Timezone = "UTC",
            BookingSlotIntervalMinutes = -15,
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.Equal(["11:00", "11:30"], result.Slots.Select(s => s.Time).ToList());
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNoAvailableResources_WhenVenueHasNoSectionsLoaded()
    {
        // Defensive guard: Sections defaults to an empty collection everywhere in production
        // (repository eager-load, entity default initializer), but the null-conditional chain
        // in the eligible-resources filter exists in case that invariant is ever violated — verify
        // it degrades to "no resources available" instead of throwing a NullReferenceException.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ReturnsNoAvailableResources_WhenVenueHasNoSectionsLoaded));
        db.Venues.Add(new Venue { Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "12:00", Timezone = "UTC" });
        db.SaveChanges();

        var restRepoMock = new Mock<IVenueRepository>();
        Venue venue = await db.Venues.FirstAsync();
        venue.Sections = null!;
        restRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(venue);

        var svc = new AvailabilityService(new BookingRepository(db), restRepoMock.Object, new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.NotEmpty(result.Slots);
        Assert.All(result.Slots, s => Assert.False(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_ExcludesResourcesFromSectionWithNoResourcesLoaded()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ExcludesResourcesFromSectionWithNoResourcesLoaded));
        db.Venues.Add(new Venue { Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "12:00", Timezone = "UTC" });
        db.Sections.Add(new Section { Id = 1, Name = "Empty", VenueId = 1 });
        db.SaveChanges();

        var restRepoMock = new Mock<IVenueRepository>();
        Venue venue = await db.Venues.Include(r => r.Sections).FirstAsync();
        venue.Sections.First().Resources = null!;
        restRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(venue);

        var svc = new AvailabilityService(new BookingRepository(db), restRepoMock.Object, new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.NotEmpty(result.Slots);
        Assert.All(result.Slots, s => Assert.False(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_HonorsExplicitEndTime_OverDefaultDurationConflictWindow()
    {
        // Mirrors GetAvailabilityAsync_UsesVenueConfiguredDuration_ForConflictWindow but for
        // the opposite branch of `b.EndTime ?? b.Date.AddMinutes(...)`: an explicit EndTime that
        // is SHORTER than the venue's default duration must govern the conflict window, not
        // the default-duration fallback.
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_HonorsExplicitEndTime_OverDefaultDurationConflictWindow));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            DefaultBookingDurationMinutes = 90,
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        // Booking has an explicit 30-minute EndTime, far shorter than the venue's
        // 90-minute default — the 12:30 slot must be free since the explicit window ends there.
        var bookingStart = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = bookingStart,
            EndTime = bookingStart.AddMinutes(30), BookingRef = "B1",
        });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, bookingStart, 2);

        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.False(slot1200.IsAvailable);

        TimeSlotDto slot1230 = result.Slots.First(s => s.Time == "12:30");
        Assert.True(slot1230.IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_IgnoresUnrecognizedOpenDaysEntry_ButKeepsValidOnes()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_IgnoresUnrecognizedOpenDaysEntry_ButKeepsValidOnes));
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC",
            // "notaday" doesn't match any ParseDayOfWeek case and parses to 0, which gets
            // filtered out by the `d > 0` guard — it should be silently ignored rather than
            // corrupting the rest of the OpenDays list.
            OpenDays = "monday,notaday",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // 2026-10-12 is a Monday — the valid entry keeps it open.
        AvailabilityResponseDto monday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.NotEmpty(monday.Slots);

        // 2026-10-13 is a Tuesday — not in OpenDays, so no slots.
        AvailabilityResponseDto tuesday = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc), 2);
        Assert.Empty(tuesday.Slots);
    }

    // ── Combinable resource groups ─────────────────────────────────────────────

    /// <summary>
    /// Seeds a venue with one 2-place standalone resource (T1) and a combinable group of two
    /// 4-place resources (T2, T3) with CombinedCapacity 8. Hours 11:00–13:00 UTC, 30-min slots.
    /// </summary>
    private static void SeedVenueWithGroup(AppDbContext db)
    {
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Group Test", OpenTime = "11:00", CloseTime = "13:00", Timezone = "UTC"
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
    public async Task GetAvailabilityAsync_AdvertisesGroup_WhenPartyLargerThanAnySingleResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_AdvertisesGroup_WhenPartyLargerThanAnySingleResource));
        SeedVenueWithGroup(db);

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Party of 6: no single resource fits (max 4), but the group (CombinedCapacity 8) does.
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 6);

        Assert.NotEmpty(result.Slots);
        // Every available slot must list the group; no single-resource ids, since all three resources
        // (capacity 2, 4 and 4) are too small for a party of 6 on their own.
        Assert.All(result.Slots.Where(s => s.IsAvailable), s =>
        {
            Assert.Contains(1, s.AvailableGroupIds);
            Assert.DoesNotContain(2, s.AvailableResourceIds);
            Assert.DoesNotContain(3, s.AvailableResourceIds);
        });
    }

    [Fact]
    public async Task GetAvailabilityAsync_RemovesGroup_WhenMemberBooked()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_RemovesGroup_WhenMemberBooked));
        SeedVenueWithGroup(db);

        var date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        // Book one member of the group at 12:00 → group can't be offered at overlapping slots.
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 2, SectionId = 1, Date = date,
            BookingRef = "B1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 6);

        // The 12:00 slot (overlaps the booking) must not advertise the group.
        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.DoesNotContain(1, slot1200.AvailableGroupIds);
    }

    [Fact]
    public async Task GetAvailabilityAsync_RemovesGroup_WhenMemberHeld()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_RemovesGroup_WhenMemberHeld));
        SeedVenueWithGroup(db);

        var holdMock = new Mock<IHoldService>();
        // Member T3 is held at 12:00 → group not offered at overlapping slots.
        holdMock.Setup(h => h.IsResourceHeld(3, It.Is<DateTime>(d => d == new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(true);

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), holdMock.Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 6);

        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.DoesNotContain(1, slot1200.AvailableGroupIds);
    }

    [Fact]
    public async Task GetAvailabilityAsync_AppliesOversizeCap_ToGroups()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_AppliesOversizeCap_ToGroups));
        SeedVenueWithGroup(db);
        var r = db.Venues.First();
        r.MaxSpareCapacity = 2; // party of 2 at an 8-place group: 8 - 2 = 6 > 2 → excluded
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.All(result.Slots, s => Assert.DoesNotContain(1, s.AvailableGroupIds));
    }

    [Fact]
    public async Task GetAvailabilityAsync_SlotAvailable_WhenOnlyGroupFits()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_SlotAvailable_WhenOnlyGroupFits));
        SeedVenueWithGroup(db);

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Party of 6: T1 (2) too small, T2/T3 (4 each) too small alone. The slot must still be
        // available via the group.
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 6);

        Assert.Contains(result.Slots, s => s.IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_StillOffersGroupMembers_Individually()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_StillOffersGroupMembers_Individually));
        SeedVenueWithGroup(db);

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Party of 4: T2 and T3 fit 4 each on their own and must stay bookable individually even
        // though they're flagged combinable — grouping them only deprioritizes them.
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 4);

        Assert.All(result.Slots.Where(s => s.IsAvailable), s =>
        {
            Assert.Contains(2, s.AvailableResourceIds);
            Assert.Contains(3, s.AvailableResourceIds);
        });
    }

    [Fact]
    public async Task GetAvailabilityAsync_NeverOffersAWalkInOnlyResource()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_NeverOffersAWalkInOnlyResource));
        SeedVenueWithGroup(db);
        db.Resources.Find(1)!.WalkInOnly = true;
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 2);

        Assert.All(result.Slots, s => Assert.DoesNotContain(1, s.AvailableResourceIds));
        Assert.All(result.Slots, s => Assert.True(s.IsAvailable));
    }

    [Fact]
    public async Task GetAvailabilityAsync_NeverOffersAGroupWithAWalkInOnlyMember()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_NeverOffersAGroupWithAWalkInOnlyMember));
        SeedVenueWithGroup(db);
        db.Resources.Find(3)!.WalkInOnly = true;
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        // Only the group fits six, so holding T3 back leaves nothing for them online.
        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 6);

        Assert.All(result.Slots, s =>
        {
            Assert.False(s.IsAvailable);
            Assert.Empty(s.AvailableGroupIds);
        });
    }

    [Fact]
    public async Task GetAvailabilityAsync_ClosesASlot_ThePartyWouldTakeOverTheGuestCap()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_ClosesASlot_ThePartyWouldTakeOverTheGuestCap));
        SeedVenueWithGroup(db);
        db.Venues.Find(1)!.MaxGuestsPerSlot = 6;
        var noon = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, PartySize = 2,
            Date = noon.AddMinutes(15), EndTime = noon.AddMinutes(75), BookingRef = "P1",
        });
        db.SaveChanges();
        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);
        var day = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        AvailabilityResponseDto fits = await svc.GetAvailabilityAsync(1, day, 4);
        AvailabilityResponseDto over = await svc.GetAvailabilityAsync(1, day, 5);

        Assert.True(fits.Slots.First(s => s.Time == "12:00").IsAvailable);
        Assert.False(over.Slots.First(s => s.Time == "12:00").IsAvailable);
        // The 12:15 start only counts against its own slot.
        Assert.True(over.Slots.First(s => s.Time == "11:30").IsAvailable);
        Assert.True(over.Slots.First(s => s.Time == "12:30").IsAvailable);
    }

    [Fact]
    public async Task GetAvailabilityAsync_RemovesGroupMember_WhenReservedByItsGroupBooking()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_RemovesGroupMember_WhenReservedByItsGroupBooking));
        SeedVenueWithGroup(db);

        var date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        // A group booking stores ResourceId = null — its member resources must still drop out of the
        // per-slot single-resource offer, or the same physical resource gets sold twice.
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceGroupId = 1, SectionId = 1, Date = date,
            BookingRef = "BG1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(
            1, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 4);

        TimeSlotDto slot1200 = result.Slots.First(s => s.Time == "12:00");
        Assert.DoesNotContain(2, slot1200.AvailableResourceIds);
        Assert.DoesNotContain(3, slot1200.AvailableResourceIds);
    }

    /// <summary>
    /// The gate the picker offers a slot through and the gate the hold endpoint judges it by
    /// have to agree. They did not: availability builds Saturday's 18:00–02:00 window and emits
    /// the after-midnight slots as part of Saturday's service, while the hold gate resolved
    /// 00:30 against Sunday and refused it whenever Sunday's schedule differed.
    /// </summary>
    [Fact]
    public async Task GetAvailabilityAsync_OnlyOffersSlotsTheHoldGateAccepts_ForAnOvernightService()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAvailabilityAsync_OnlyOffersSlotsTheHoldGateAccepts_ForAnOvernightService));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Late",
            OpenTime = "18:00",
            CloseTime = "02:00",
            OpenDays = "6", // Saturday only, so Sunday carries the tail but opens nothing itself
            Timezone = "UTC",
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.SaveChanges();

        var svc = new AvailabilityService(new BookingRepository(db), new VenueRepository(db), new Mock<IHoldService>().Object);
        var saturday = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        AvailabilityResponseDto result = await svc.GetAvailabilityAsync(1, saturday, 2);
        Venue venue = db.Venues.Find(1)!;

        Assert.Contains(result.Slots, s => s.Time == "00:30");
        Assert.All(result.Slots, slot =>
        {
            DateTime slotUtc = SlotInstant(saturday, slot.Time);
            Assert.True(venue.IsOpenAt(slotUtc), $"availability offered {slot.Time} but the hold gate refuses it");
        });
    }

    /// <summary>A slot time past the opening hour belongs to the same day; one before it has wrapped.</summary>
    private static DateTime SlotInstant(DateTime localDate, string slotTime)
    {
        TimeSpan timeOfDay = TimeSpan.Parse(slotTime, System.Globalization.CultureInfo.InvariantCulture);
        return timeOfDay >= TimeSpan.FromHours(18) ? localDate + timeOfDay : localDate.AddDays(1) + timeOfDay;
    }
}
