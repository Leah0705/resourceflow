using Microsoft.Data.Sqlite;
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

public partial class AdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Mock<IHoldService> _holdServiceMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();

    public AdminServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AdminService CreateService()
    {
        return new AdminService(
            new BookingRepository(_db),
            new BookingFilterRepository(_db),
            new VenueRepository(_db),
            new SectionRepository(_db),
            new ResourceRepository(_db),
            _holdServiceMock.Object,
            _emailServiceMock.Object);
    }

    private AdminService CreateServiceWithNotifications(INotificationQueue notificationQueue)
    {
        return new AdminService(
            new BookingRepository(_db),
            new BookingFilterRepository(_db),
            new VenueRepository(_db),
            new SectionRepository(_db),
            new ResourceRepository(_db),
            _holdServiceMock.Object,
            _emailServiceMock.Object,
            brandService: null,
            notificationQueue: notificationQueue);
    }

    private AdminService CreateService(ICurrentUserService currentUser)
    {
        return new AdminService(
            new BookingRepository(_db),
            new BookingFilterRepository(_db),
            new VenueRepository(_db),
            new SectionRepository(_db),
            new ResourceRepository(_db),
            _holdServiceMock.Object,
            _emailServiceMock.Object,
            brandService: null,
            notificationQueue: null,
            audit: null,
            currentUser: currentUser);
    }

    private void SeedBase(int venueId = 1)
    {
        _db.Venues.Add(new Venue { Id = venueId, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = venueId, Name = "Main", VenueId = venueId });
        _db.Resources.Add(new Resource { Id = venueId, Name = "T1", Capacity = 4, SectionId = venueId });
    }

    [Fact]
    public async Task GetOverviewAsync_CountsBookingsStrandedByEveryLocationsSchedule()
    {
        AdminService svc = CreateService();
        // Two Monday-only locations, each holding one Monday slot and one Tuesday slot.
        // Only the Tuesdays are stranded, and the count has to span both locations.
        _db.Venues.Add(new Venue { Id = 1, Name = "A", Timezone = "UTC", OpenDays = "1" });
        _db.Venues.Add(new Venue { Id = 2, Name = "B", Timezone = "UTC", OpenDays = "1" });
        var monday = new DateTime(2099, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var tuesday = new DateTime(2099, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, Date = monday, PartySize = 2, BookingRef = "a1" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, Date = tuesday, PartySize = 2, BookingRef = "a2" });
        _db.Bookings.Add(new Booking { Id = 3, VenueId = 2, Date = tuesday, PartySize = 2, BookingRef = "b1" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(2, overview.ScheduleConflictsCount);
        Assert.Equal([1, 2], overview.ScheduleConflictLocationIds);
    }

    [Fact]
    public async Task GetOverviewAsync_ReportsTodaysGuestsPerSlot_ForACappedLocation()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue
        {
            Id = 1, Name = "Capped", Timezone = "UTC", OpenTime = "00:00", CloseTime = "23:59", MaxGuestsPerSlot = 8,
        });
        _db.Venues.Add(new Venue { Id = 2, Name = "Uncapped", Timezone = "UTC" });
        DateTime today = DateTime.UtcNow.Date;
        _db.Bookings.AddRange(
            new Booking { VenueId = 1, Date = today.AddHours(19), PartySize = 4, BookingRef = "c1" },
            new Booking { VenueId = 1, Date = today.AddHours(19).AddMinutes(20), PartySize = 3, BookingRef = "c2" },
            new Booking { VenueId = 1, Date = today.AddHours(20), PartySize = 2, BookingRef = "c3" },
            new Booking { VenueId = 1, Date = today.AddHours(20), PartySize = 6, BookingRef = "c4", IsCancelled = true },
            new Booking { VenueId = 2, Date = today.AddHours(19), PartySize = 4, BookingRef = "u1" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        LocationPacingDto pacing = Assert.Single(overview.TodayPacing);
        Assert.Equal(8, pacing.MaxGuestsPerSlot);
        Assert.Equal(["19:00", "20:00"], pacing.Slots.Select(s => s.Time));
        Assert.Equal([7, 2], pacing.Slots.Select(s => s.Guests));
    }

    [Fact]
    public async Task GetOverviewAsync_CountsNoConflicts_WhenEveryBookingStillFits()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "A", Timezone = "UTC", OpenDays = "1,2,3,4,5,6,7" });
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            Date = new DateTime(2099, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            PartySize = 2,
            BookingRef = "a1",
        });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(0, overview.ScheduleConflictsCount);
        Assert.Empty(overview.ScheduleConflictLocationIds);
    }

    [Fact]
    public async Task GetOverviewAsync_NamesOnlyTheLocationsHoldingAStrandedBooking()
    {
        AdminService svc = CreateService();
        // One location has narrowed to Mondays and kept a Tuesday slot; the other still opens
        // every day. A count alone cannot say which, so the ids are what makes it actionable.
        _db.Venues.Add(new Venue { Id = 1, Name = "Fits", Timezone = "UTC", OpenDays = "1,2,3,4,5,6,7" });
        _db.Venues.Add(new Venue { Id = 2, Name = "Stranded", Timezone = "UTC", OpenDays = "1" });
        var tuesday = new DateTime(2099, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, Date = tuesday, PartySize = 2, BookingRef = "a1" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 2, Date = tuesday, PartySize = 2, BookingRef = "b1" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal([2], overview.ScheduleConflictLocationIds);
    }

    [Fact]
    public async Task GetOverviewAsync_TotalCapacity_HandlesNull()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        // No bookings, totalCapacity should be 0
        AdminOverviewDto overview = await svc.GetOverviewAsync();
        Assert.Equal(0, overview.TotalCapacity);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyData_HasSevenElements()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(7, overview.OccupancyData.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyData_AllZeroWhenNoBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.All(overview.OccupancyData, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyData_PeakDayIs100Percent()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        // Add 3 bookings today (the peak day) and 1 booking yesterday
        _db.Bookings.Add(new Booking { Id = 10, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "TODAY1" });
        _db.Bookings.Add(new Booking { Id = 11, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(13), BookingRef = "TODAY2" });
        _db.Bookings.Add(new Booking { Id = 12, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(14), BookingRef = "TODAY3" });
        _db.Bookings.Add(new Booking { Id = 13, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddDays(-1).AddHours(12), BookingRef = "YEST" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        // Today (index 6) has 3 bookings — the peak — so it should be 100%
        Assert.Equal(100, overview.OccupancyData[6]);
        // Yesterday (index 5) has 1 booking out of 3 peak → ~33%
        Assert.Equal(33, overview.OccupancyData[5]);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyData_NormalizesRelativeToPeakNotResources()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        // Add extra resources — should NOT affect histogram normalization
        _db.Resources.Add(new Resource { Id = 10, Name = "T10", Capacity = 4, SectionId = 1 });
        _db.Resources.Add(new Resource { Id = 11, Name = "T11", Capacity = 4, SectionId = 1 });
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 20, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        // 1 booking today; peak = 1 → today should be 100% regardless of resource count
        Assert.Equal(100, overview.OccupancyData[6]);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyData_ExcludesCancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 30, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "ACTIVE", IsCancelled = false });
        _db.Bookings.Add(new Booking { Id = 31, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(13), BookingRef = "CANCELLED", IsCancelled = true });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        // Only 1 non-cancelled booking today; that is the peak → 100%
        Assert.Equal(100, overview.OccupancyData[6]);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyDates_HasSevenElements()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(7, overview.OccupancyDates.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyDates_LastIsToday()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(DateTime.UtcNow.Date.ToString("yyyy-MM-dd"), overview.OccupancyDates[6]);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyDates_AreIsoDateFormat()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.All(overview.OccupancyDates, d => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", d));
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyDates_AlignedWithOccupancyData()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(overview.OccupancyData.Count, overview.OccupancyDates.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyCounts_HasSevenElements()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(7, overview.OccupancyCounts.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyCounts_AlignedWithOccupancyData()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(overview.OccupancyData.Count, overview.OccupancyCounts.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyCounts_MatchRawBookingsAtPeakDay()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        // 3 bookings today (peak), 1 yesterday
        _db.Bookings.Add(new Booking { Id = 40, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "T1" });
        _db.Bookings.Add(new Booking { Id = 41, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(13), BookingRef = "T2" });
        _db.Bookings.Add(new Booking { Id = 42, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(14), BookingRef = "T3" });
        _db.Bookings.Add(new Booking { Id = 43, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddDays(-1).AddHours(12), BookingRef = "Y1" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        // Today (index 6) is the peak: 3 raw bookings, normalized to 100%.
        Assert.Equal(3, overview.OccupancyCounts[6]);
        Assert.Equal(100, overview.OccupancyData[6]);
        Assert.Equal(1, overview.OccupancyCounts[5]);
    }

    [Fact]
    public async Task GetOverviewAsync_OccupancyCounts_AllZeroWhenNoBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.All(overview.OccupancyCounts, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task GetOverviewAsync_TodayBookingsList_ContainsTodayBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "TODAY", IsCancelled = false });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.NotNull(overview.TodayBookingsList);
        Assert.Single(overview.TodayBookingsList);
        Assert.Equal("TODAY", overview.TodayBookingsList[0].BookingRef);
    }

    [Fact]
    public async Task GetOverviewAsync_TodayBookingsList_ExcludesCancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "ACTIVE", IsCancelled = false });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(13), BookingRef = "CANCELLED", IsCancelled = true });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Single(overview.TodayBookingsList);
        Assert.Equal("ACTIVE", overview.TodayBookingsList[0].BookingRef);
    }

    [Fact]
    public async Task GetOverviewAsync_TodayBookingsList_ExcludesYesterdayBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddDays(-1), BookingRef = "YESTERDAY" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Empty(overview.TodayBookingsList);
    }

    [Fact]
    public async Task GetOverviewAsync_TodayBookingsList_CountMatchesTodayBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(12), BookingRef = "B1" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(13), BookingRef = "B2" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(overview.TodayBookings, overview.TodayBookingsList.Count);
    }

    [Fact]
    public async Task GetOverviewAsync_TodayBookingsList_OrderedByDate()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(14), BookingRef = "LATE" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.Date.AddHours(10), BookingRef = "EARLY" });
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(2, overview.TodayBookingsList.Count);
        Assert.Equal("EARLY", overview.TodayBookingsList[0].BookingRef);
        Assert.Equal("LATE", overview.TodayBookingsList[1].BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_GlobalPastFilter_Works()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        SeedBase(2);
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(-5), BookingRef = "PAST" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 2, SectionId = 2, ResourceId = 2, Date = nowUtc.AddHours(5), BookingRef = "FUTURE" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> past = await svc.GetBookingsAsync(null, null, "past");
        Assert.Single(past);
        Assert.Equal("PAST", past[0].BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_CancelledFilter_Works()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "CANCELLED", IsCancelled = true });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> cancelled = await svc.GetBookingsAsync(1, null, "cancelled");
        Assert.Single(cancelled);
        Assert.Equal("CANCELLED", cancelled[0].BookingRef);

        List<BookingDetailDto> globalCancelled = await svc.GetBookingsAsync(null, null, "cancelled");
        Assert.Single(globalCancelled);
    }

    [Fact]
    public async Task GetBookingsAsync_AllFilter_Works()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(-5), BookingRef = "PAST" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(5), BookingRef = "FUTURE" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> all = await svc.GetBookingsAsync(1, null, "all");
        Assert.Equal(2, all.Count);

        List<BookingDetailDto> globalAll = await svc.GetBookingsAsync(null, null, "all");
        Assert.Equal(2, globalAll.Count);
    }

    [Fact]
    public async Task GetBookingsAsync_WithDateFilter_NoVenue_Works()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime today = DateTime.UtcNow.Date;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(12), BookingRef = "TODAY" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(null, today, "all");
        Assert.Single(results);
    }

    [Fact]
    public async Task GetBookingsAsync_EmailFilter_IsCaseInsensitiveAndPartial()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "A", CustomerEmail = "Alice@Example.com" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B", CustomerEmail = "bob@example.com" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(1, null, "all", email: "alice");

        Assert.Single(results);
        Assert.Equal("A", results[0].BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_BookingRefFilter_IsCaseInsensitiveAndPartial()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "ABC123" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "XYZ789" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(1, null, "all", bookingRef: "abc");

        Assert.Single(results);
        Assert.Equal("ABC123", results[0].BookingRef);
    }

    // The admin lookup used to split the typed term into "email if it has an @, else booking
    // reference", so a partial email matched nothing and a customer name matched nothing at all.
    // One free-text param spans all three fields instead.
    [Theory]
    [InlineData("ali")]           // partial name
    [InlineData("ALICE@EX")]      // partial email, wrong case
    [InlineData("bc12")]          // mid-string slice of the booking reference
    public async Task GetBookingsAsync_QueryFilter_MatchesNameEmailAndRefPartially(string query)
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "ABC123", CustomerName = "Alice Smith", CustomerEmail = "Alice@Example.com" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "XYZ789", CustomerName = "Bob Jones", CustomerEmail = "bob@example.com" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(1, null, "all", query: query);

        Assert.Equal("ABC123", Assert.Single(results).BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_QueryFilter_ReturnsNothingWhenTheTermMatchesNoField()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "ABC123", CustomerName = "Alice Smith", CustomerEmail = "alice@example.com" });
        await _db.SaveChangesAsync();

        Assert.Empty(await svc.GetBookingsAsync(1, null, "all", query: "zzz"));
    }

    [Fact]
    public async Task GetBookingAsync_ReturnsNull_WhenNotFound()
    {
        AdminService svc = CreateService();
        BookingDetailDto? result = await svc.GetBookingAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenResourceNotFound()
    {
        AdminService svc = CreateService();
        var req = new AdminCreateBookingRequest { VenueId = 1, SectionId = 1, ResourceId = 999 };
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateBookingAsync(req));
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenVenueMismatch()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Venues.Add(new Venue { Id = 2, Name = "Other" });
        await _db.SaveChangesAsync();
        var req = new AdminCreateBookingRequest { VenueId = 2, SectionId = 1, ResourceId = 1 };
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateBookingAsync(req));
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenConflict()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        var req = new AdminCreateBookingRequest { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 };
        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(req));
    }

    [Fact]
    public async Task CreateBookingAsync_PlacesAWalkIn_AtAWalkInOnlyResourceInAFullSlot()
    {
        // Staff decide at the front desk: neither the held-back resource nor the guest cap stops them.
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        Venue venue = _db.Venues.Single();
        venue.MaxGuestsPerSlot = 2;
        _db.Resources.Single().WalkInOnly = true;
        DateTime date = DateTime.UtcNow.Date.AddDays(2).AddHours(19);
        _db.Bookings.Add(new Booking { VenueId = 1, PartySize = 2, Date = date, EndTime = date.AddHours(1), BookingRef = "FULL" });
        await _db.SaveChangesAsync();

        BookingDetailDto result = await svc.CreateBookingAsync(new AdminCreateBookingRequest
        {
            VenueId = 1, SectionId = 1, ResourceId = 1, PartySize = 4, Date = date, CustomerEmail = "walkin@example.com",
        });

        Assert.Equal(1, result.ResourceId);
    }

    [Fact]
    public async Task CreateBookingAsync_Throws_WhenPartySizeExceedCapacity()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        var req = new AdminCreateBookingRequest { VenueId = 1, SectionId = 1, ResourceId = 1, PartySize = 10, Date = DateTime.UtcNow };
        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(req));
    }

    [Theory]
    [InlineData(BookingRefFormat.Numeric, true)]
    [InlineData(BookingRefFormat.AlphaNumeric, false)]
    public async Task CreateBookingAsync_UsesTheVenuesBookingRefFormat(
        BookingRefFormat format, bool expectDigits)
    {
        // Admin-recorded walk-ins mint their reference on a third call site, independent of
        // BookingService — a staff-entered booking must read the same way as a customer's.
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", BookingRefFormat = format });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        await _db.SaveChangesAsync();

        BookingDetailDto result = await svc.CreateBookingAsync(new AdminCreateBookingRequest
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
            PartySize = 2
        });

        Assert.Equal(expectDigits, result.BookingRef!.All(char.IsAsciiDigit));
    }

    // ── Configurable booking duration ───────────────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(480)]
    public async Task CreateBookingAsync_EndTime_UsesVenueConfiguredDuration(int durationMinutes)
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = durationMinutes });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        await _db.SaveChangesAsync();

        DateTime date = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var req = new AdminCreateBookingRequest { VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, PartySize = 2 };

        BookingDetailDto result = await svc.CreateBookingAsync(req);

        Assert.Equal(date.AddMinutes(durationMinutes), result.EndTime);
    }

    [Fact]
    public async Task CreateBookingAsync_ConflictCheck_UsesVenueConfiguredDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 120 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime newStart = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        // Existing (later) booking starts 100 minutes after the new booking's requested start —
        // outside a fixed 60-minute conflict window, but inside the venue's configured
        // 120-minute occupancy window for the new booking.
        _db.Bookings.Add(new Booking { VenueId = 1, SectionId = 1, ResourceId = 1, Date = newStart.AddMinutes(100), EndTime = newStart.AddMinutes(100).AddMinutes(120), BookingRef = "LATER1" });
        await _db.SaveChangesAsync();

        var req = new AdminCreateBookingRequest { VenueId = 1, SectionId = 1, ResourceId = 1, Date = newStart, PartySize = 2 };

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateBookingAsync(req));
    }

    // ── Admin past-date creation ────────────────────────────────────────────
    // The admin path intentionally has NO past-date guard (unlike the customer
    // path in BookingService). This test locks that behaviour in so a future
    // change cannot accidentally copy the customer guard into the admin path.
    [Fact]
    public async Task CreateBookingAsync_Succeeds_WithPastDate_AndPersistsAsConfirmed()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 60 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        await _db.SaveChangesAsync();

        // One week in the past — the customer path rejects this, admin must allow it.
        DateTime pastStart = DateTime.UtcNow.AddDays(-7);
        var req = new AdminCreateBookingRequest
        {
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            Date = pastStart,
            PartySize = 2,
            CustomerEmail = "admin@example.com",
        };

        BookingDetailDto result = await svc.CreateBookingAsync(req);

        Assert.False(result.IsCancelled);
        Assert.Equal(pastStart, result.Date, TimeSpan.FromSeconds(1));
        Assert.Equal(pastStart.AddMinutes(60), result.EndTime!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExtendBookingAsync_FallsBackToVenueConfiguredDuration_WhenEndTimeInvalid()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 90 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime? newEnd = await svc.ExtendBookingAsync(1, 30);
        Assert.Equal(date.AddMinutes(90).AddMinutes(30), newEnd);
    }

    [Fact]
    public async Task ExtendBookingAsync_UsesStoredEndTime_WhenAlreadyValid()
    {
        // Covers the "no fallback needed" branch: EndTime is present and after Date, so it
        // must be used as-is rather than recomputed from the venue's configured duration.
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddMinutes(30), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime? newEnd = await svc.ExtendBookingAsync(1, 15);

        Assert.Equal(date.AddMinutes(45), newEnd);
    }

    [Fact]
    public async Task ExtendBookingAsync_FallsBackToSixtyMinutes_WhenVenueNoLongerExists()
    {
        // ExtendBookingAsync fetches the booking via FindByIdAsync (no eager-loaded Venue
        // navigation) and then looks up the venue separately — an orphaned VenueId
        // legitimately returns null here, exercising the `?? 60` default duration fallback. FK
        // enforcement (on by default for this connection) is disabled so the dangling
        // VenueId can be inserted at all.
        AdminService svc = CreateService();
        DateTime date = DateTime.UtcNow;
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 999, SectionId = null, ResourceId = null, Date = date, EndTime = date.AddHours(-1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime? newEnd = await svc.ExtendBookingAsync(1, 30);

        Assert.Equal(date.AddMinutes(60).AddMinutes(30), newEnd);
    }

    [Fact]
    public async Task CancelBookingAsync_NotifiesQueue_WithVenueName_WhenVenueExists()
    {
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        var notificationQueue = new Mock<INotificationQueue>();
        AdminService svc = CreateServiceWithNotifications(notificationQueue.Object);

        Assert.True(await svc.CancelBookingAsync(1));

        notificationQueue.Verify(n => n.EnqueueBookingCancelled(It.IsAny<Booking>(), "Test"), Times.Once);
    }

    [Fact]
    public async Task CancelBookingAsync_UsesEmptyVenueName_WhenVenueNoLongerExists()
    {
        // The re-fetch inside the notification branch (GetByIdAsync) Include()s the required
        // Venue navigation, which EF Core compiles to an inner join — deleting the
        // venue row directly makes that re-fetch return null entirely, driving both the
        // `withVenue ?? booking` and `?.Venue?.Name ?? ""` fallbacks. FK enforcement
        // (on by default for this connection) is turned off first so the raw DELETE doesn't
        // cascade-remove the booking along with its venue.
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(1), BookingRef = "B1" });
        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Venues WHERE Id = 1");

        var notificationQueue = new Mock<INotificationQueue>();
        AdminService svc = CreateServiceWithNotifications(notificationQueue.Object);

        Assert.True(await svc.CancelBookingAsync(1));

        notificationQueue.Verify(n => n.EnqueueBookingCancelled(It.IsAny<Booking>(), ""), Times.Once);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenPartySizeExceedCapacity()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1", PartySize = 2 });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { PartySize = 10 };
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.AdminUpdateBookingAsync(1, req));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenResourceNotFound()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { ResourceId = 999 };
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdminUpdateBookingAsync(1, req));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenSectionIdChangedWithoutResourceId()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Sections.Add(new Section { Id = 2, Name = "Annex", VenueId = 1 });
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { SectionId = 2 };
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdminUpdateBookingAsync(1, req));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_AllowsUnchangedSectionId_WhenOnlyOtherFieldsUpdated()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime newDate = DateTime.UtcNow.AddHours(2);
        var req = new AdminUpdateBookingRequest { SectionId = 1, ResourceId = 1, Date = newDate };
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, req);

        Assert.NotNull(result);
        Assert.Equal(newDate, result!.Date, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_TrimsCustomerName()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { CustomerName = "  Jane Doe  " };
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, req);

        Assert.NotNull(result);
        Assert.Equal("Jane Doe", result!.CustomerName);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_ClearsCustomerName_WhenWhitespaceOnly()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1", CustomerName = "Existing Name" });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { CustomerName = "   " };
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, req);

        Assert.NotNull(result);
        Assert.Null(result!.CustomerName);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_AdjustsEndTime_WhenDateChanges()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime original = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = original, EndTime = original.AddHours(2), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime newDate = original.AddDays(1);
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = newDate });
        Assert.Equal(newDate.AddHours(2), result!.EndTime);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_SetsDefaultEndTime_WhenOriginalEndTimeNull()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime original = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = original, EndTime = null, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime newDate = original.AddDays(1);
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = newDate });
        Assert.Equal(newDate.AddHours(1), result!.EndTime);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_FixesInvalidEndTime()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { CustomerEmail = "new@test.com" });
        Assert.True(result!.EndTime > result.Date);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_SetsDefaultEndTime_UsingVenueConfiguredDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 90 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime original = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = original, EndTime = null, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime newDate = original.AddDays(1);
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = newDate });
        Assert.Equal(newDate.AddMinutes(90), result!.EndTime);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_FixesInvalidEndTime_UsingVenueConfiguredDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 90 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { CustomerEmail = "new@test.com" });
        Assert.Equal(date.AddMinutes(90), result!.EndTime);
    }

    // Editing a booking is how it gets moved — there is no separate reschedule flow — so this
    // guard is the only thing between an admin changing a date and the same resource being booked
    // twice. It compared the request against a booking the caller had already written the request
    // onto, so every comparison was a value against itself and the check never ran once.
    [Fact]
    public async Task AdminUpdateBookingAsync_RejectsAMoveOntoAResourceAlreadyBookedThen()
    {
        AdminService svc = CreateService();
        SeedTwoBookingsOnOneResource(out DateTime _, out DateTime taken);
        await _db.SaveChangesAsync();

        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = taken }));

        Assert.Contains("conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_AllowsAMoveOntoAFreeSlotOnTheSameResource()
    {
        AdminService svc = CreateService();
        SeedTwoBookingsOnOneResource(out DateTime original, out DateTime _);
        await _db.SaveChangesAsync();
        DateTime free = original.AddDays(5);

        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = free });

        Assert.Equal(free, result!.Date);
    }

    // The booking being moved must not be found conflicting with itself, or every edit that left
    // the slot alone (a name or party-size change) would report a conflict with its own row.
    [Fact]
    public async Task AdminUpdateBookingAsync_DoesNotTreatTheBookingItselfAsAConflict()
    {
        AdminService svc = CreateService();
        SeedTwoBookingsOnOneResource(out DateTime original, out DateTime _);
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(
            1,
            new AdminUpdateBookingRequest { Date = original, CustomerName = "Renamed" });

        Assert.Equal("Renamed", result!.CustomerName);
        Assert.Equal(original, result.Date);
    }

    // A group booking stores ResourceId = null. Checking resources alone would compare it against every
    // other resource-less booking on the instance and miss the group it actually occupies.
    [Fact]
    public async Task AdminUpdateBookingAsync_RejectsAMoveOntoAGroupWhoseMemberIsBookedThen()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 60 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        _db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        _db.ResourceGroups.Add(new ResourceGroup { Id = 1, VenueId = 1, Name = "Window desks", CombinedCapacity = 7 });
        _db.ResourceGroupMemberships.Add(new ResourceGroupMembership { ResourceGroupId = 1, ResourceId = 1 });
        _db.ResourceGroupMemberships.Add(new ResourceGroupMembership { ResourceGroupId = 1, ResourceId = 2 });

        var original = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var taken = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        // The mover reserves the whole group; a single member is already taken at the target slot.
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceGroupId = 1, Date = original, EndTime = original.AddMinutes(60), BookingRef = "MOVING" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 2, Date = taken, EndTime = taken.AddMinutes(60), BookingRef = "MEMBER" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = taken }));
    }

    /// <summary>Booking 1 is the one being moved; booking 2 already holds <paramref name="taken"/> on the same resource.</summary>
    private void SeedTwoBookingsOnOneResource(out DateTime original, out DateTime taken)
    {
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 60 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });

        original = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        taken = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = original, EndTime = original.AddMinutes(60), BookingRef = "MOVING" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = taken, EndTime = taken.AddMinutes(60), BookingRef = "EXISTING" });
    }

    [Fact]
    public async Task GetVenuesAsync_ActiveBookingsCount_UsesVenueConfiguredDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 120 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime nowUtc = DateTime.UtcNow;
        // Started 90 minutes ago, no EndTime — still active under the venue's configured
        // 120-minute duration, but would have already "ended" under the old fixed 60-minute assumption.
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-90), EndTime = null, BookingRef = "ACTIVE1" });
        await _db.SaveChangesAsync();

        List<LookupDto> venues = await svc.GetVenuesAsync();

        Assert.Equal(1, venues.Single(r => r.Id == 1).ActiveBookingsCount);
    }

    [Fact]
    public async Task ExtendAllActiveBookingsAsync_UsesVenueConfiguredDuration_ForMissingEndTimeFallback()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 120 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime nowUtc = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-90), EndTime = null, BookingRef = "ACTIVE1" });
        await _db.SaveChangesAsync();

        List<BookingDetailDto>? result = await svc.ExtendAllActiveBookingsAsync(1, 15);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(nowUtc.AddMinutes(-90).AddMinutes(120).AddMinutes(15), result[0].EndTime);
    }

    [Fact]
    public async Task ExtendBookingAsync_ReturnsNull_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.Null(await svc.ExtendBookingAsync(999, 30));
    }

    [Fact]
    public async Task ExtendBookingAsync_FallsBackToDate_WhenEndTimeInvalid()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = date.AddHours(-1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime? newEnd = await svc.ExtendBookingAsync(1, 30);
        Assert.Equal(date.AddHours(1).AddMinutes(30), newEnd);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsFalse_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.False(await svc.CancelBookingAsync(999));
    }

    [Fact]
    public async Task CancelBookingAsync_Throws_WhenBookingDateIsInThePast()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow.AddHours(-1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelBookingAsync(1));

        Booking inDb = await _db.Bookings.FirstAsync(b => b.Id == 1);
        Assert.False(inDb.IsCancelled);
    }

    [Fact]
    public async Task CancelBookingAsync_Succeeds_WithinFiveMinuteGracePeriod()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow.AddMinutes(-4);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        Assert.True(await svc.CancelBookingAsync(1));
    }

    [Fact]
    public async Task CancelBookingAsync_Throws_JustOutsideFiveMinuteGracePeriod()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime date = DateTime.UtcNow.AddMinutes(-6);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelBookingAsync(1));
    }

    [Fact]
    public async Task PurgeBookingAsync_ReturnsFalse_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.False(await svc.PurgeBookingAsync(999));
    }

    [Fact]
    public async Task PurgeBookingAsync_DeletesBooking_AndReturnsTrue()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        Assert.True(await svc.PurgeBookingAsync(1));
        Assert.Null(await _db.Bookings.FindAsync(1));
    }

    [Fact]
    public async Task RestoreBookingAsync_ReturnsNull_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.Null(await svc.RestoreBookingAsync(999));
    }

    [Fact]
    public async Task RestoreBookingAsync_Throws_WhenAlreadyActive()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, IsCancelled = false, BookingRef = "B1" });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.RestoreBookingAsync(1));
    }

    [Fact]
    public async Task RestoreBookingAsync_ReactivatesCancelledBooking()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow,
            IsCancelled = true, CancelledAt = DateTime.UtcNow, BookingRef = "B1",
        });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.RestoreBookingAsync(1);

        Assert.NotNull(result);
        Assert.False(result!.IsCancelled);
        Booking inDb = await _db.Bookings.SingleAsync(b => b.Id == 1);
        Assert.False(inDb.IsCancelled);
        Assert.Null(inDb.CancelledAt);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsTrue_WhenAlreadyCancelled()
    {
        // Idempotent re-cancel: IsCancelled short-circuits the "already passed" guard so a
        // second cancel attempt on the same booking succeeds instead of throwing.
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddDays(-3),
            IsCancelled = true, CancelledAt = DateTime.UtcNow.AddDays(-2), BookingRef = "B1",
        });
        await _db.SaveChangesAsync();

        Assert.True(await svc.CancelBookingAsync(1));
    }

    [Fact]
    public async Task ExtendBookingAsync_FallsBackToVenueConfiguredDuration_WhenEndTimeMissing()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", DefaultBookingDurationMinutes = 90 });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime date = DateTime.UtcNow;
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = date, EndTime = null, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime? newEnd = await svc.ExtendBookingAsync(1, 30);

        Assert.Equal(date.AddMinutes(90).AddMinutes(30), newEnd);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_ReturnsNull_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.Null(await svc.AdminUpdateBookingAsync(999, new AdminUpdateBookingRequest()));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenInvalidVenue()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { VenueId = 999 }));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenInvalidResource()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { ResourceId = 999 }));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenInvalidSection()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { SectionId = 999 }));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_Throws_WhenPartySizeExceedsNewResourceCapacity()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 2, SectionId = 1 });
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow, BookingRef = "B1", PartySize = 4 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { ResourceId = 2, PartySize = 3 }));
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_SkipsCapacityCheck_WhenResolvedResourceNoLongerExists()
    {
        // booking.ResourceId points at a resource row that no longer exists (removed directly here,
        // bypassing the normal DeleteResourceAsync FK-null flow) — resolvedResourceId still carries a
        // value, but FindByIdAsync(999) returns null, so the capacity guard must be skipped
        // instead of throwing a NullReferenceException on currentResource.Capacity. FK enforcement
        // (on by default for this connection) is disabled so the dangling ResourceId can be
        // inserted at all.
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 999, Date = DateTime.UtcNow, BookingRef = "B1", PartySize = 2 });
        await _db.SaveChangesAsync();

        var req = new AdminUpdateBookingRequest { PartySize = 6 };
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, req);

        Assert.NotNull(result);
        Assert.Equal(6, result!.PartySize);
    }

    [Fact]
    public async Task AdminUpdateBookingAsync_HandlesNullEndTime_OnDateChange()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime original = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = original, EndTime = null, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        DateTime newDate = original.AddHours(5);
        BookingDetailDto? result = await svc.AdminUpdateBookingAsync(1, new AdminUpdateBookingRequest { Date = newDate });
        Assert.Equal(newDate.AddHours(1), result!.EndTime);
    }

    [Fact]
    public async Task GetVenuesAsync_ReturnsList()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        List<LookupDto> list = await svc.GetVenuesAsync();
        Assert.Single(list);
    }

    // Regression: ActiveBookingsCount was missing b.Date <= nowUtc, causing future
    // bookings to be counted as active and inflating the count shown in the Extend modal.
    [Fact]
    public async Task GetVenuesAsync_ActiveBookingsCount_ExcludesFutureBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        // Booking that started 30 min ago and has not ended → ACTIVE
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-30), EndTime = nowUtc.AddMinutes(30), BookingRef = "CURRENT" });
        // Booking starting in 2 hours → NOT active yet
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(2), EndTime = nowUtc.AddHours(3), BookingRef = "FUTURE" });
        await _db.SaveChangesAsync();

        List<LookupDto> list = await svc.GetVenuesAsync();

        Assert.Equal(1, list[0].ActiveBookingsCount);
    }

    [Fact]
    public async Task GetVenuesAsync_ActiveBookingsCount_ExcludesCancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-30), EndTime = nowUtc.AddMinutes(30), BookingRef = "ACTIVE" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-20), EndTime = nowUtc.AddMinutes(40), BookingRef = "CANCELLED", IsCancelled = true });
        await _db.SaveChangesAsync();

        List<LookupDto> list = await svc.GetVenuesAsync();

        Assert.Equal(1, list[0].ActiveBookingsCount);
    }

    [Fact]
    public async Task GetVenuesAsync_UpcomingBookingsCount_CountsOnlyFutureUncancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(-2), EndTime = nowUtc.AddHours(-1), BookingRef = "PAST" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-15), EndTime = nowUtc.AddMinutes(45), BookingRef = "INPROGRESS" });
        _db.Bookings.Add(new Booking { Id = 3, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddDays(1), BookingRef = "FUTURE" });
        _db.Bookings.Add(new Booking { Id = 4, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddDays(2), BookingRef = "CANCELLED", IsCancelled = true });
        await _db.SaveChangesAsync();

        List<LookupDto> list = await svc.GetVenuesAsync();

        Assert.Equal(1, list[0].UpcomingBookingsCount);
    }

    [Fact]
    public async Task GetVenuesAsync_ActiveBookingsCount_ExcludesPastBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        // Booking that ended 1 hour ago
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(-2), EndTime = nowUtc.AddHours(-1), BookingRef = "PAST" });
        // Current booking
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-15), EndTime = nowUtc.AddMinutes(45), BookingRef = "CURRENT" });
        await _db.SaveChangesAsync();

        List<LookupDto> list = await svc.GetVenuesAsync();

        Assert.Equal(1, list[0].ActiveBookingsCount);
    }

    [Fact]
    public async Task GetVenuesAsync_ActiveBookingsCount_MatchesExtendAllActiveBookings()
    {
        // The count shown in the UI before extending should equal the number of bookings
        // that ExtendAllActiveBookingsAsync will actually extend.
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-30), EndTime = nowUtc.AddMinutes(30), BookingRef = "CURRENT" });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(2), EndTime = nowUtc.AddHours(3), BookingRef = "FUTURE" });
        _db.Bookings.Add(new Booking { Id = 3, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(-3), EndTime = nowUtc.AddHours(-2), BookingRef = "PAST" });
        await _db.SaveChangesAsync();

        List<LookupDto> venueList = await svc.GetVenuesAsync();
        List<BookingDetailDto>? extended = await svc.ExtendAllActiveBookingsAsync(1, 30);

        Assert.Equal(venueList[0].ActiveBookingsCount, extended!.Count);
    }

    [Fact]
    public async Task GetSectionsAsync_ReturnsList()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        List<LookupDto> list = await svc.GetSectionsAsync(1);
        Assert.Single(list);
    }

    // ── SortOrder / reorderable sections ─────────────────────────────────────

    [Fact]
    public async Task GetSectionsAsync_OrdersBySortOrder_NotAlphabetically()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "Zebra", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "Alpha", VenueId = 1, SortOrder = 1 });
        await _db.SaveChangesAsync();

        List<LookupDto> list = await svc.GetSectionsAsync(1);

        Assert.Equal(["Zebra", "Alpha"], list.Select(s => s.Name));
    }

    [Fact]
    public async Task GetResourcesAsync_OrdersSectionsBySortOrder_NotAlphabetically()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "Zebra", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "Alpha", VenueId = 1, SortOrder = 1 });
        await _db.SaveChangesAsync();

        List<SectionDto>? result = await svc.GetResourcesAsync(1);

        Assert.NotNull(result);
        Assert.Equal(["Zebra", "Alpha"], result!.Select(s => s.Name));
    }

    [Fact]
    public async Task ReorderSectionsAsync_PersistsNewOrder_AndReadBackIsCorrect()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "First", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "Second", VenueId = 1, SortOrder = 1 });
        _db.Sections.Add(new Section { Id = 3, Name = "Third", VenueId = 1, SortOrder = 2 });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, [3, 1, 2]);

        Assert.True(result);
        List<LookupDto> list = await svc.GetSectionsAsync(1);
        Assert.Equal(["Third", "First", "Second"], list.Select(s => s.Name));
    }

    [Fact]
    public async Task ReorderSectionsAsync_MoveUpSwap_PersistsCorrectly()
    {
        // Simulates the up/down-button UX: moving "Second" up swaps it with "First".
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "First", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "Second", VenueId = 1, SortOrder = 1 });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, [2, 1]);

        Assert.True(result);
        Assert.Equal(0, (await _db.Sections.FindAsync(2))!.SortOrder);
        Assert.Equal(1, (await _db.Sections.FindAsync(1))!.SortOrder);
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsNull_WhenVenueNotFound()
    {
        AdminService svc = CreateService();
        Assert.Null(await svc.ReorderSectionsAsync(999, [1, 2]));
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsFalse_WhenSectionCountMismatch()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        Assert.False(await svc.ReorderSectionsAsync(1, [1, 2]));
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsFalse_WhenSectionIdBelongsToDifferentVenue()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "R1", Timezone = "UTC" });
        _db.Venues.Add(new Venue { Id = 2, Name = "R2", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "S1", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "S2", VenueId = 2, SortOrder = 0 });
        await _db.SaveChangesAsync();

        Assert.False(await svc.ReorderSectionsAsync(1, [2]));
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsFalse_WhenDuplicateIdsProvided()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "First", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "Second", VenueId = 1, SortOrder = 1 });
        await _db.SaveChangesAsync();

        Assert.False(await svc.ReorderSectionsAsync(1, [1, 1]));
    }

    [Fact]
    public async Task ReorderSectionsAsync_Succeeds_WithSingleSection()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "Only", VenueId = 1, SortOrder = 0 });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, [1]);

        Assert.True(result);
        Assert.Equal(0, (await _db.Sections.FindAsync(1))!.SortOrder);
    }

    [Fact]
    public async Task ReorderSectionsAsync_Succeeds_WhenVenueHasZeroSections_AndEmptyListProvided()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, []);

        Assert.True(result);
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsFalse_WhenVenueHasZeroSections_ButIdsProvided()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        await _db.SaveChangesAsync();

        Assert.False(await svc.ReorderSectionsAsync(1, [1]));
    }

    [Fact]
    public async Task ReorderSectionsAsync_ReturnsFalse_WhenSectionIdsIsNull()
    {
        // Regression guard: System.Text.Json overwrites the request DTO's
        // `= new()` field initializer with null when the client sends an explicit
        // `"sectionIds": null`. Without a guard, that null reaches sectionIds.Count /
        // .Distinct() below and throws an unhandled NullReferenceException (500) instead
        // of the clean 400 the rest of the method is designed to return.
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "First", VenueId = 1, SortOrder = 0 });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, null!);

        Assert.False(result);
    }

    [Fact]
    public async Task ReorderSectionsAsync_OnlyAffectsTargetVenue_WhenReorderingConcurrently()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "R1", Timezone = "UTC" });
        _db.Venues.Add(new Venue { Id = 2, Name = "R2", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "R1-First", VenueId = 1, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 2, Name = "R1-Second", VenueId = 1, SortOrder = 1 });
        _db.Sections.Add(new Section { Id = 3, Name = "R2-First", VenueId = 2, SortOrder = 0 });
        _db.Sections.Add(new Section { Id = 4, Name = "R2-Second", VenueId = 2, SortOrder = 1 });
        await _db.SaveChangesAsync();

        bool? result = await svc.ReorderSectionsAsync(1, [2, 1]);

        Assert.True(result);
        Assert.Equal(0, (await _db.Sections.FindAsync(2))!.SortOrder);
        Assert.Equal(1, (await _db.Sections.FindAsync(1))!.SortOrder);
        Assert.Equal(0, (await _db.Sections.FindAsync(3))!.SortOrder);
        Assert.Equal(1, (await _db.Sections.FindAsync(4))!.SortOrder);
    }

    [Fact]
    public async Task CreateVenueAsync_Works()
    {
        AdminService svc = CreateService();
        VenueDto r = await svc.CreateVenueAsync(" New ", " Addr ");
        Assert.Equal("New", r.Name);
        Assert.Equal("Addr", r.Address);
    }

    [Fact]
    public async Task CreateVenueAsync_AllowsNullAddress()
    {
        AdminService svc = CreateService();
        VenueDto r = await svc.CreateVenueAsync("New", null);
        Assert.Null(r.Address);
    }

    [Fact]
    public async Task DeleteVenueAsync_ReturnsFalse_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.False(await svc.DeleteVenueAsync(999));
    }

    [Fact]
    public async Task DeleteVenueAsync_Throws_WhenNotArchived()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Live", Timezone = "UTC" });
        await _db.SaveChangesAsync();

        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => svc.DeleteVenueAsync(1));

        Assert.Contains("Archive this location", ex.Message, StringComparison.Ordinal);
        Assert.True(await _db.Venues.AnyAsync(r => r.Id == 1));
    }

    [Fact]
    public async Task DeleteVenueAsync_DeletesVenueAndBookings_WhenArchived()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Gone", Timezone = "UTC", IsArchived = true });
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            Date = DateTime.UtcNow.AddDays(1),
            PartySize = 2,
            CustomerName = "A",
            CustomerEmail = "a@example.com",
        });
        await _db.SaveChangesAsync();

        Assert.True(await svc.DeleteVenueAsync(1));
        Assert.False(await _db.Venues.AnyAsync(r => r.Id == 1));
        Assert.False(await _db.Bookings.AnyAsync(b => b.VenueId == 1));
    }

    [Fact]
    public async Task GetVenueDeletePreviewAsync_ReturnsNull_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.Null(await svc.GetVenueDeletePreviewAsync(999));
    }

    [Fact]
    public async Task GetVenueDeletePreviewAsync_CountsTheFullCascade()
    {
        AdminService svc = CreateService();
        DateTime nowUtc = DateTime.UtcNow;
        _db.Venues.Add(new Venue { Id = 1, Name = "Downtown", Timezone = "UTC", IsArchived = true });
        _db.Venues.Add(new Venue { Id = 2, Name = "Other", Timezone = "UTC" });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Sections.Add(new Section { Id = 2, Name = "Annex", VenueId = 1 });
        _db.Sections.Add(new Section { Id = 3, Name = "Elsewhere", VenueId = 2 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        _db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 2, SectionId = 1 });
        _db.Resources.Add(new Resource { Id = 3, Name = "T3", Capacity = 4, SectionId = 3 });
        _db.ResourceGroups.Add(new ResourceGroup { Id = 1, VenueId = 1, CombinedCapacity = 4 });
        _db.Bookings.Add(NewBooking(1, 1, nowUtc.AddDays(1), cancelled: false));
        _db.Bookings.Add(NewBooking(2, 1, nowUtc.AddDays(2), cancelled: false));
        _db.Bookings.Add(NewBooking(3, 1, nowUtc.AddDays(3), cancelled: true));
        _db.Bookings.Add(NewBooking(4, 1, nowUtc.AddDays(-1), cancelled: false));
        _db.Bookings.Add(NewBooking(5, 2, nowUtc.AddDays(1), cancelled: false));
        await _db.SaveChangesAsync();

        VenueDeletePreviewDto preview = (await svc.GetVenueDeletePreviewAsync(1))!;

        Assert.Equal("Downtown", preview.Name);
        Assert.True(preview.IsArchived);
        Assert.Equal(2, preview.SectionCount);
        Assert.Equal(2, preview.ResourceCount);
        Assert.Equal(1, preview.ResourceGroupCount);
        Assert.Equal(4, preview.BookingCount);
        // Cancelled and past bookings are in the cascade but are not "upcoming".
        Assert.Equal(2, preview.UpcomingBookingCount);
    }

    private static Booking NewBooking(int id, int venueId, DateTime dateUtc, bool cancelled) => new()
    {
        Id = id,
        VenueId = venueId,
        Date = dateUtc,
        PartySize = 2,
        CustomerName = "Guest",
        CustomerEmail = "guest@example.com",
        IsCancelled = cancelled,
    };

    [Fact]
    public async Task SetArchivedAsync_ReturnsFalse_WhenNotFound()
    {
        AdminService svc = CreateService();
        Assert.False(await svc.SetArchivedAsync(999, true));
    }

    [Fact]
    public async Task SetArchivedAsync_ArchivesVenue_AndReturnsTrue()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        await _db.SaveChangesAsync();

        bool result = await svc.SetArchivedAsync(1, true);

        Assert.True(result);
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        Assert.True(venue.IsArchived);
    }

    [Fact]
    public async Task SetArchivedAsync_UnarchivesVenue_WhenArchivedFalse()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC", IsArchived = true });
        await _db.SaveChangesAsync();

        bool result = await svc.SetArchivedAsync(1, false);

        Assert.True(result);
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        Assert.False(venue.IsArchived);
    }

    [Fact]
    public async Task GetResourcesAsync_ReturnsNull_WhenNoSections()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test" });
        await _db.SaveChangesAsync();
        Assert.Null(await svc.GetResourcesAsync(1));
    }

    [Fact]
    public async Task GetOverviewAsync_HandlesInvalidTimezone()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "Invalid/Zone" });
        await _db.SaveChangesAsync();
        AdminOverviewDto result = await svc.GetOverviewAsync();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ToDetailDto_HandlesUnspecifiedKind()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime local = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = local, EndTime = local.AddHours(1), BookingRef = "B1" });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.GetBookingAsync(1);
        Assert.Equal(DateTimeKind.Utc, result!.Date.Kind);
        Assert.Equal(DateTimeKind.Utc, result.EndTime!.Value.Kind);
    }

    [Fact]
    public async Task ToDetailDto_HandlesUtcKind_ForCancelledAt()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(-2),
            BookingRef = "B1", IsCancelled = true, CancelledAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.GetBookingAsync(1);

        Assert.NotNull(result!.CancelledAt);
        Assert.Equal(DateTimeKind.Utc, result.CancelledAt!.Value.Kind);
    }

    [Fact]
    public async Task ToDetailDto_SpecifiesUtcKind_ForCancelledAt_WhenStoredAsUnspecified()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        DateTime unspecifiedCancelledAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        _db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = DateTime.UtcNow.AddHours(-2),
            BookingRef = "B1", IsCancelled = true, CancelledAt = unspecifiedCancelledAt,
        });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.GetBookingAsync(1);

        Assert.NotNull(result!.CancelledAt);
        Assert.Equal(DateTimeKind.Utc, result.CancelledAt!.Value.Kind);
    }

    [Fact]
    public async Task ToDetailDto_UsesGenericFallbackNames_WhenResourceAndSectionUnassigned()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = null, ResourceId = null, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.GetBookingAsync(1);

        Assert.Equal("Section", result!.SectionName);
        Assert.Equal("Resource", result.ResourceName);
    }

    [Fact]
    public async Task ToDetailDto_UsesIdQualifiedFallbackNames_WhenResourceAndSectionRowsAreMissing()
    {
        // SectionId/ResourceId are set but no matching row exists (e.g. removed independently of
        // the normal delete flow) — the fallback must include the numeric id for identifiability.
        // FK enforcement (on by default for this connection) is disabled so the dangling ids
        // can be inserted at all.
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue { Id = 1, Name = "Test", Timezone = "UTC" });
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 42, ResourceId = 99, Date = DateTime.UtcNow, BookingRef = "B1" });
        await _db.SaveChangesAsync();

        BookingDetailDto? result = await svc.GetBookingAsync(1);

        Assert.Equal("Section 42", result!.SectionName);
        Assert.Equal("Resource 99", result.ResourceName);
    }

    [Fact]
    public async Task GetBookingsAsync_ActiveFilter_ExcludesCompletedBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        // 1. Just started (10 mins ago) - should be ACTIVE
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-10), BookingRef = "LIVE" });

        // 2. Started 2 hours ago - should be PAST
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-120), BookingRef = "OLD" });

        // 3. Future - should be ACTIVE
        _db.Bookings.Add(new Booking { Id = 3, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(2), BookingRef = "FUTURE" });

        await _db.SaveChangesAsync();

        List<BookingDetailDto> active = await svc.GetBookingsAsync(1, null, "active");
        List<BookingDetailDto> past = await svc.GetBookingsAsync(1, null, "past");

        Assert.Equal(2, active.Count);
        Assert.Contains(active, b => b.BookingRef == "LIVE");
        Assert.Contains(active, b => b.BookingRef == "FUTURE");

        Assert.Single(past);
        Assert.Equal("OLD", past[0].BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_GridMode_ShowsAllBookingsForDay()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime today = DateTime.UtcNow.Date;

        // Morning booking (now past)
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(8), BookingRef = "MORNING" });
        // Evening booking (future)
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(20), BookingRef = "EVENING" });

        await _db.SaveChangesAsync();

        // When requesting a specific date, both should show up regardless of current time
        List<BookingDetailDto> results = await svc.GetBookingsAsync(1, today, "active");

        Assert.Equal(2, results.Count);
    }

    // Regression test: dashboard's "Today's Bookings" was showing cancelled bookings
    // because getAdminDashboardStats called the API with status=all instead of status=active.
    // When status=active + a date are both supplied, the backend enters isGridMode which
    // applies !b.IsCancelled — this test verifies that behaviour.
    [Fact]
    public async Task GetBookingsAsync_ActiveStatusWithDate_ExcludesCancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime today = DateTime.UtcNow.Date;

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(12), BookingRef = "ACTIVE", IsCancelled = false });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(14), BookingRef = "CANCELLED", IsCancelled = true, CancelledAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(null, today, "active");

        Assert.Single(results);
        Assert.Equal("ACTIVE", results[0].BookingRef);
    }

    [Fact]
    public async Task GetBookingsAsync_AllStatusWithDate_IncludesCancelledBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime today = DateTime.UtcNow.Date;

        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(12), BookingRef = "ACTIVE", IsCancelled = false });
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = today.AddHours(14), BookingRef = "CANCELLED", IsCancelled = true, CancelledAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> results = await svc.GetBookingsAsync(null, today, "all");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ExtendAllActiveBookingsAsync_OnlyExtendsCurrentlyActiveBookings()
    {
        AdminService svc = CreateService();
        SeedBase(1);

        DateTime nowUtc = DateTime.UtcNow;

        // 1. Currently active (started 30 mins ago)
        _db.Bookings.Add(new Booking { Id = 1, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddMinutes(-30), BookingRef = "ACTIVE" });

        // 2. Future (starting in 1 hour)
        _db.Bookings.Add(new Booking { Id = 2, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(1), BookingRef = "FUTURE" });

        // 3. Past (ended 1 hour ago)
        _db.Bookings.Add(new Booking { Id = 3, VenueId = 1, SectionId = 1, ResourceId = 1, Date = nowUtc.AddHours(-3), EndTime = nowUtc.AddHours(-1), BookingRef = "PAST" });

        await _db.SaveChangesAsync();

        List<BookingDetailDto>? results = await svc.ExtendAllActiveBookingsAsync(1, 60);

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("ACTIVE", results[0].BookingRef);

        // Verify the active booking was extended (default 1h -> 2h)
        Booking? activeBooking = await _db.Bookings.FindAsync(1);
        Assert.Equal(nowUtc.AddMinutes(-30).AddMinutes(60).AddMinutes(60), activeBooking!.EndTime);

        // Verify the future booking was NOT extended
        Booking? futureBooking = await _db.Bookings.FindAsync(2);
        Assert.Null(futureBooking!.EndTime);
    }

    [Fact]
    public async Task PauseVenueBookingsAsync_ReturnsFalse_WhenVenueNotFound()
    {
        AdminService svc = CreateService();
        bool result = await svc.PauseVenueBookingsAsync(999, 60);
        Assert.False(result);
    }

    [Fact]
    public async Task PauseVenueBookingsAsync_SetsBookingsPausedUntil()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        bool result = await svc.PauseVenueBookingsAsync(1, 60);

        Assert.True(result);
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        Assert.NotNull(venue.BookingsPausedUntil);
        Assert.True(venue.BookingsPausedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task UnpauseVenueBookingsAsync_ReturnsFalse_WhenVenueNotFound()
    {
        AdminService svc = CreateService();
        bool result = await svc.UnpauseVenueBookingsAsync(999);
        Assert.False(result);
    }

    [Fact]
    public async Task UnpauseVenueBookingsAsync_ClearsBookingsPausedUntil()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        await svc.PauseVenueBookingsAsync(1, 60);

        bool result = await svc.UnpauseVenueBookingsAsync(1);

        Assert.True(result);
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        Assert.Null(venue.BookingsPausedUntil);
    }

    [Fact]
    public async Task ExtendAllActiveBookingsAsync_ReturnsNull_WhenVenueNotFound()
    {
        AdminService svc = CreateService();
        List<BookingDetailDto>? result = await svc.ExtendAllActiveBookingsAsync(999, 30);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOverviewAsync_CountsPausedVenues()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        venue.BookingsPausedUntil = DateTime.UtcNow.AddHours(1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(1, overview.PausedVenuesCount);
    }

    [Fact]
    public async Task GetOverviewAsync_DoesNotCountVenues_WithExpiredPause()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();
        Venue venue = await _db.Venues.SingleAsync(r => r.Id == 1);
        venue.BookingsPausedUntil = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        AdminOverviewDto overview = await svc.GetOverviewAsync();

        Assert.Equal(0, overview.PausedVenuesCount);
    }

    [Fact]
    public async Task GetBookingsAsync_UpcomingFilter_BehavesLikeActive()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        List<BookingDetailDto> upcoming = await svc.GetBookingsAsync(1, null, "upcoming");
        List<BookingDetailDto> active = await svc.GetBookingsAsync(1, null, "active");

        Assert.Equal(active.Count, upcoming.Count);
    }

    [Fact]
    public async Task GetBookingsAsync_UnrecognizedStatus_DefaultsToActive()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        await _db.SaveChangesAsync();

        List<BookingDetailDto> unrecognized = await svc.GetBookingsAsync(1, null, "not-a-real-status");
        List<BookingDetailDto> active = await svc.GetBookingsAsync(1, null, "active");

        Assert.Equal(active.Count, unrecognized.Count);
    }
}
