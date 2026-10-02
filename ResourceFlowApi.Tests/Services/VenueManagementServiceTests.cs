using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

public class VenueManagementServiceTests
{
    private static VenueManagementService CreateService(AppDbContext db, ICurrentUserService? currentUser = null) => new(
        new VenueRepository(db),
        new SectionRepository(db),
        new ResourceRepository(db),
        new BookingRepository(db),
        new ResourceGroupRepository(db),
        audit: null,
        currentUser: currentUser);

    [Fact]
    public async Task GetScheduleConflictsAsync_ReturnsNull_WhenVenueNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_ReturnsNull_WhenVenueNotFound));
        Assert.Null(await CreateService(db).GetScheduleConflictsAsync(999));
    }

    [Fact]
    public async Task GetScheduleConflictsAsync_FlagsBookingsLeftOutsideNarrowedHours()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_FlagsBookingsLeftOutsideNarrowedHours));
        // Taken while the location opened at 11:00; it now opens at 17:00.
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 12);

        List<ScheduleConflictDto>? conflicts = await CreateService(db).GetScheduleConflictsAsync(1);

        ScheduleConflictDto conflict = Assert.Single(conflicts!);
        Assert.Equal("outsideHours", conflict.Reason);
        Assert.Equal("ABC123", conflict.BookingRef);
        Assert.Equal("Ada", conflict.CustomerName);
        Assert.Equal(2, conflict.PartySize);
    }

    // This DTO carries CustomerName too, so it needs the same
    // BookingGuestVisibility redaction as the booking-read paths in AdminService/BookingService.
    [Fact]
    public async Task GetScheduleConflictsAsync_RedactsCustomerName_ForAnApiKeyWithoutGuestsRead()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_RedactsCustomerName_ForAnApiKeyWithoutGuestsRead));
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 12);

        List<ScheduleConflictDto>? conflicts = await CreateService(
            db, FakeCurrentUser.ApiKey((ApiKeyScopes.Locations, ApiKeyScopes.Read))).GetScheduleConflictsAsync(1);

        Assert.Null(Assert.Single(conflicts!).CustomerName);
    }

    [Fact]
    public async Task GetScheduleConflictsAsync_ReturnsCustomerName_ForAnApiKeyWithGuestsRead()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_ReturnsCustomerName_ForAnApiKeyWithGuestsRead));
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 12);

        List<ScheduleConflictDto>? conflicts = await CreateService(
            db,
            FakeCurrentUser.ApiKey(
                (ApiKeyScopes.Locations, ApiKeyScopes.Read),
                (ApiKeyScopes.Guests, ApiKeyScopes.Read))).GetScheduleConflictsAsync(1);

        Assert.Equal("Ada", Assert.Single(conflicts!).CustomerName);
    }

    [Fact]
    public async Task GetScheduleConflictsAsync_IgnoresBookingsThatStillFit()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_IgnoresBookingsThatStillFit));
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 19);

        Assert.Empty((await CreateService(db).GetScheduleConflictsAsync(1))!);
    }

    [Fact]
    public async Task GetScheduleConflictsAsync_IgnoresCancelledBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_IgnoresCancelledBookings));
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 12, cancelled: true);

        Assert.Empty((await CreateService(db).GetScheduleConflictsAsync(1))!);
    }

    [Fact]
    public async Task GetScheduleConflictsAsync_IgnoresBookingsAlreadyInThePast()
    {
        // A slot that has already happened cannot be moved, so it is not the admin's problem.
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_IgnoresBookingsAlreadyInThePast));
        await SeedScheduleFixtureAsync(db, openTime: "17:00", bookingHourUtc: 12, daysAhead: -7);

        Assert.Empty((await CreateService(db).GetScheduleConflictsAsync(1))!);
    }

    // Turning a location walk-in-only stops it taking new online bookings; it does not close it,
    // so the slots already on the books stand. Staff-recorded walk-ins live at exactly such a
    // location, and reporting them would give it a conflict count that never reaches zero.
    [Fact]
    public async Task GetScheduleConflictsAsync_KeepsBookingsWhenTheLocationTurnsWalkInOnly()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetScheduleConflictsAsync_KeepsBookingsWhenTheLocationTurnsWalkInOnly));
        await SeedScheduleFixtureAsync(db, bookingHourUtc: 19, walkInOnly: true);

        Assert.Empty((await CreateService(db).GetScheduleConflictsAsync(1))!);
    }

    /// <summary>
    /// One upcoming booking at <paramref name="bookingHourUtc"/> against a UTC location whose
    /// schedule is whatever the caller passes — i.e. the schedule as edited <em>after</em> the
    /// booking was taken.
    /// </summary>
    private static async Task SeedScheduleFixtureAsync(
        AppDbContext db,
        int bookingHourUtc,
        string openTime = "11:00",
        bool walkInOnly = false,
        bool cancelled = false,
        int daysAhead = 7)
    {
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R1",
            Timezone = "UTC",
            OpenTime = openTime,
            CloseTime = "23:00",
            OpenDays = "1,2,3,4,5,6,7",
            WalkInOnly = walkInOnly,
        });

        DateTime day = DateTime.UtcNow.Date.AddDays(daysAhead);
        db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            Date = day.AddHours(bookingHourUtc),
            PartySize = 2,
            CustomerName = "Ada",
            BookingRef = "ABC123",
            IsCancelled = cancelled,
            CancelledAt = cancelled ? DateTime.UtcNow : null,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAll()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAllAsync_ReturnsAll));
        db.Venues.Add(new Venue { Id = 1, Name = "R1" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        List<VenueDto> result = await svc.GetAllAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.GetByIdAsync(999));
    }

    [Fact]
    public async Task CreateAsync_HandlesNestedEntities()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_HandlesNestedEntities));
        var svc = CreateService(db);
        var dto = new VenueDto
        {
            Name = "New",
            Sections = [new SectionDto { Name = "S1", Resources = [new ResourceDto { Name = "T1", Capacity = 4 }] }]
        };
        VenueDto result = await svc.CreateAsync(dto);
        Assert.Single(result.Sections);
        Assert.Single(result.Sections[0].Resources);
    }

    [Fact]
    public async Task CreateAsync_HonorsCallerSuppliedOpenTimeCloseTimeOpenDaysAndTimezone()
    {
        // Regression test (see the comment above the mapping in CreateAsync): hours/days/
        // timezone the client sends must be persisted, not silently reverted to the
        // "OpenTime omitted" defaults — those defaults should only kick in when the caller
        // truly omits the field (see CreateAsync_HandlesNestedEntities and friends, which all
        // omit these fields and so only exercise the "use default" branch).
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_HonorsCallerSuppliedOpenTimeCloseTimeOpenDaysAndTimezone));
        var svc = CreateService(db);
        var dto = new VenueDto
        {
            Name = "New",
            OpenTime = "08:00",
            CloseTime = "20:00",
            OpenDays = "1,3,5",
            Timezone = "America/New_York",
        };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal("08:00", result.OpenTime);
        Assert.Equal("20:00", result.CloseTime);
        Assert.Equal("1,3,5", result.OpenDays);
        Assert.Equal("America/New_York", result.Timezone);

        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal("08:00", entity!.OpenTime);
        Assert.Equal("20:00", entity.CloseTime);
        Assert.Equal("1,3,5", entity.OpenDays);
        Assert.Equal("America/New_York", entity.Timezone);
    }

    [Fact]
    public async Task CreateAsync_AppliesPerDayOpenHours_FromDto()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_AppliesPerDayOpenHours_FromDto));
        var svc = CreateService(db);
        var dto = new VenueDto
        {
            Name = "New",
            OpenHours =
            [
                new DayHoursDto { Day = 6, Open = "12:00", Close = "16:00" },
                new DayHoursDto { Day = 7, Open = "12:00", Close = "16:00" },
            ],
        };

        VenueDto result = await svc.CreateAsync(dto);

        DayHoursDto saturday = result.OpenHours!.Single(h => h.Day == 6);
        Assert.Equal("12:00", saturday.Open);
        Assert.Equal("16:00", saturday.Close);

        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.NotNull(entity!.OpenHoursJson);
    }

    [Fact]
    public async Task CreateAsync_AssignsSequentialSortOrder_ToBulkCreatedSections()
    {
        // Regression test: CreateAsync previously omitted SortOrder from the
        // bulk section-creation mapping, so every section created through this path defaulted
        // to SortOrder = 0 instead of reflecting the order the caller supplied them in.
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_AssignsSequentialSortOrder_ToBulkCreatedSections));
        var svc = CreateService(db);
        var dto = new VenueDto
        {
            Name = "New",
            Sections =
            [
                new SectionDto { Name = "First", Resources = [] },
                new SectionDto { Name = "Second", Resources = [] },
                new SectionDto { Name = "Third", Resources = [] },
            ]
        };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal([0, 1, 2], result.Sections.Select(s => s.SortOrder));
    }

    [Fact]
    public async Task CreateAsync_CopiesDefaultBookingDurationMinutes_FromDto()
    {
        // Regression test: CreateAsync previously omitted
        // DefaultBookingDurationMinutes from the field-by-field entity mapping, silently
        // discarding any caller-supplied value and always persisting the entity default (60).
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_CopiesDefaultBookingDurationMinutes_FromDto));
        var svc = CreateService(db);
        var dto = new VenueDto { Name = "New", DefaultBookingDurationMinutes = 90 };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal(90, result.DefaultBookingDurationMinutes);
        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal(90, entity!.DefaultBookingDurationMinutes);
    }

    [Fact]
    public async Task CreateAsync_CopiesBookingSlotIntervalMinutes_FromDto()
    {
        // Regression test: CreateAsync must map BookingSlotIntervalMinutes through,
        // not silently drop it to the entity default (30).
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_CopiesBookingSlotIntervalMinutes_FromDto));
        var svc = CreateService(db);
        var dto = new VenueDto { Name = "New", BookingSlotIntervalMinutes = 15 };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal(15, result.BookingSlotIntervalMinutes);
        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal(15, entity!.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task CreateAsync_CopiesMaxResourceOversizeCapacity_FromDto()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_CopiesMaxResourceOversizeCapacity_FromDto));
        var svc = CreateService(db);
        var dto = new VenueDto { Name = "New", MaxSpareCapacity = 2 };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal(2, result.MaxSpareCapacity);
        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal(2, entity!.MaxSpareCapacity);
    }

    [Fact]
    public async Task CreateAsync_LeavesMaxResourceOversizeCapacityNull_WhenDtoOmitsIt()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateAsync_LeavesMaxResourceOversizeCapacityNull_WhenDtoOmitsIt));
        var svc = CreateService(db);
        var dto = new VenueDto { Name = "New" };

        VenueDto result = await svc.CreateAsync(dto);

        // Null means "off/unrestricted" — the default for new and existing venues.
        Assert.Null(result.MaxSpareCapacity);
    }

    [Fact]
    public async Task CreateAsync_CopiesBookingRefFormat_FromDto()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_CopiesBookingRefFormat_FromDto));
        var svc = CreateService(db);
        var dto = new VenueDto { Name = "New", BookingRefFormat = "Numeric" };

        VenueDto result = await svc.CreateAsync(dto);

        Assert.Equal("Numeric", result.BookingRefFormat);
        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal(BookingRefFormat.Numeric, entity!.BookingRefFormat);
    }

    [Fact]
    public async Task CreateAsync_DefaultsBookingRefFormatToAlphaNumeric_WhenDtoOmitsIt()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(CreateAsync_DefaultsBookingRefFormatToAlphaNumeric_WhenDtoOmitsIt));
        var svc = CreateService(db);

        VenueDto result = await svc.CreateAsync(new VenueDto { Name = "New" });

        Assert.Equal("AlphaNumeric", result.BookingRefFormat);
        Venue? entity = await db.Venues.FindAsync(result.Id);
        Assert.Equal(BookingRefFormat.AlphaNumeric, entity!.BookingRefFormat);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownBookingRefFormat()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_RejectsUnknownBookingRefFormat));
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new VenueDto { Name = "New", BookingRefFormat = "Roman" }));
    }

    [Theory]
    [InlineData("Numeric", BookingRefFormat.Numeric)]
    [InlineData("numeric", BookingRefFormat.Numeric)]
    [InlineData("AlphaNumeric", BookingRefFormat.AlphaNumeric)]
    public async Task UpdateAsync_PersistsBookingRefFormat(string sent, BookingRefFormat expected)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_PersistsBookingRefFormat)}_{sent}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            BookingRefFormat = sent
        });

        Assert.Equal(expected.ToString(), result!.BookingRefFormat);
        Venue entity = await db.Venues.SingleAsync();
        Assert.Equal(expected, entity.BookingRefFormat);
    }

    [Fact]
    public async Task UpdateAsync_LeavesBookingRefFormatUntouched_WhenRequestOmitsIt()
    {
        using AppDbContext db = TestDbFactory.Create(
            nameof(UpdateAsync_LeavesBookingRefFormatUntouched_WhenRequestOmitsIt));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            BookingRefFormat = BookingRefFormat.Numeric
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R" });

        Assert.Equal("Numeric", result!.BookingRefFormat);
        Venue entity = await db.Venues.SingleAsync();
        Assert.Equal(BookingRefFormat.Numeric, entity.BookingRefFormat);
    }

    [Theory]
    [InlineData("Roman")]
    [InlineData("")]
    [InlineData("1")]
    public async Task UpdateAsync_RejectsUnknownBookingRefFormat(string sent)
    {
        // "1" is rejected deliberately: Enum.TryParse would happily accept the underlying
        // number, but the wire contract is the member names only.
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_RejectsUnknownBookingRefFormat)}_{sent}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", BookingRefFormat = sent }));
    }

    [Fact]
    public async Task UpdateAsync_PersistsMaxResourceOversizeCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_PersistsMaxResourceOversizeCapacity));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            MaxSpareCapacity = 1
        });

        Assert.Equal(1, result!.MaxSpareCapacity);
        Venue entity = await db.Venues.SingleAsync();
        Assert.Equal(1, entity.MaxSpareCapacity);
    }

    [Fact]
    public async Task UpdateAsync_ClearsMaxResourceOversizeCapacity_WhenRequestIsNull()
    {
        // The settings form always sends the field (number or null); null must clear a
        // previously-set cap so the admin can toggle the setting "Off".
        using AppDbContext db = TestDbFactory.Create(
            nameof(UpdateAsync_ClearsMaxResourceOversizeCapacity_WhenRequestIsNull));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", MaxSpareCapacity = 2 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            MaxSpareCapacity = null
        });

        Assert.Null(result!.MaxSpareCapacity);
        Venue entity = await db.Venues.SingleAsync();
        Assert.Null(entity.MaxSpareCapacity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task UpdateAsync_SetsOrClearsMaxGuestsPerSlot(int? cap)
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_SetsOrClearsMaxGuestsPerSlot) + cap);
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", MaxGuestsPerSlot = 20 });
        await db.SaveChangesAsync();

        VenueDto? result = await CreateService(db).UpdateAsync(1, new UpdateVenueRequest { Name = "R", MaxGuestsPerSlot = cap });

        Assert.Equal(cap, result!.MaxGuestsPerSlot);
        Assert.Equal(cap, (await db.Venues.SingleAsync()).MaxGuestsPerSlot);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAMaxGuestsPerSlotOfZero()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_RejectsAMaxGuestsPerSlotOfZero));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService(db).UpdateAsync(1, new UpdateVenueRequest { Name = "R", MaxGuestsPerSlot = 0 }));

        Assert.Equal(ErrorCodes.VenueMaxGuestsInvalid, ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_RejectsNegativeMaxResourceOversizeCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_RejectsNegativeMaxResourceOversizeCapacity));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            MaxSpareCapacity = -1
        }));
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_ReturnsNull_WhenNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.UpdateAsync(999, new UpdateVenueRequest()));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_UpdatesFields));
        db.Venues.Add(new Venue { Id = 1, Name = "Old", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "New",
            OpenTime = "09:00",
            CloseTime = "22:00",
            OpenDays = "1,2,3",
            Timezone = "GMT"
        });
        Assert.Equal("New", result!.Name);
        Assert.Equal("GMT", result.Timezone);
    }

    // ── Description blurb ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Persists_Description_Trimmed()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Persists_Description_Trimmed));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            Description = "  A cozy spot with [guide](https://example.com) links.  "
        });

        Assert.NotNull(result);
        Assert.Equal("A cozy spot with [guide](https://example.com) links.", result.Description);
    }

    [Fact]
    public async Task UpdateAsync_Clears_Description_WhenBlank()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Clears_Description_WhenBlank));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", Description = "Old blurb" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            Description = "   "
        });

        Assert.NotNull(result);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task UpdateAsync_Persists_GuideUrl_Trimmed()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Persists_GuideUrl_Trimmed));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            GuideUrl = "  https://example.com/guide.pdf  "
        });

        Assert.NotNull(result);
        Assert.Equal("https://example.com/guide.pdf", result.GuideUrl);
    }

    [Fact]
    public async Task UpdateAsync_Clears_GuideUrl_WhenBlank()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Clears_GuideUrl_WhenBlank));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", GuideUrl = "https://old.example.com/guide.pdf" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            GuideUrl = "   "
        });

        Assert.NotNull(result);
        Assert.Null(result.GuideUrl);
    }

    [Theory]
    [InlineData("asdf")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/guide.pdf")]
    [InlineData("mailto:hello@example.com")]
    public async Task UpdateAsync_RejectsInvalidGuideUrl(string guideUrl)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_RejectsInvalidGuideUrl)}_{guideUrl}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            GuideUrl = guideUrl,
        }));

        // Unchanged — validation throws before assignment.
        Assert.Null((await db.Venues.FindAsync(1))!.GuideUrl);
    }

    [Fact]
    public async Task UpdateAsync_AcceptsServedGuidePath()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_AcceptsServedGuidePath));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        // The /media/guide-<id>.pdf path is written by MediaService.UploadGuideAsync and must
        // not be rejected by URL validation (it's a relative served-file path, not absolute).
        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            GuideUrl = "/media/guide-1.pdf?v=123",
        });

        Assert.NotNull(result);
        Assert.Equal("/media/guide-1.pdf?v=123", result.GuideUrl);
    }

    [Fact]
    public async Task UpdateAsync_AcceptsExternalHttpsGuideUrl()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_AcceptsExternalHttpsGuideUrl));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            GuideUrl = "https://example.com/guide.pdf",
        });

        Assert.NotNull(result);
        Assert.Equal("https://example.com/guide.pdf", result.GuideUrl);
    }

    // ── Per-venue contact info ────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Persists_ContactFields_Trimmed()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Persists_ContactFields_Trimmed));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            PhoneNumber = "  +44 20 7946 0958  ",
            EmailAddress = "  hello@example.com  ",
        });

        Assert.NotNull(result);
        Assert.Equal("+44 20 7946 0958", result.PhoneNumber);
        Assert.Equal("hello@example.com", result.EmailAddress);

        Venue stored = (await db.Venues.FindAsync(1))!;
        Assert.Equal("+44 20 7946 0958", stored.PhoneNumber);
        Assert.Equal("hello@example.com", stored.EmailAddress);
    }

    [Fact]
    public async Task UpdateAsync_Clears_ContactFields_WhenBlank()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Clears_ContactFields_WhenBlank));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            PhoneNumber = "+44 20 7946 0958",
            EmailAddress = "old@example.com",
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            PhoneNumber = "",
            EmailAddress = "   ",
        });

        Assert.NotNull(result);
        Assert.Null(result.PhoneNumber);
        Assert.Null(result.EmailAddress);
    }

    [Fact]
    public async Task UpdateAsync_Keeps_ContactFields_WhenNull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Keeps_ContactFields_WhenNull));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            PhoneNumber = "+44 20 7946 0958",
            EmailAddress = "keep@example.com",
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R" });

        Assert.NotNull(result);
        Assert.Equal("+44 20 7946 0958", result.PhoneNumber);
        Assert.Equal("keep@example.com", result.EmailAddress);
    }

    [Fact]
    public async Task UpdateAsync_RejectsOversizedPhoneNumber()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_RejectsOversizedPhoneNumber));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            PhoneNumber = new string('9', ContactLimits.MaxPhoneLength + 1),
        }));

        Assert.Null((await db.Venues.FindAsync(1))!.PhoneNumber);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    public async Task UpdateAsync_RejectsMalformedEmailAddress(string email)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_RejectsMalformedEmailAddress)}_{email}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            EmailAddress = email,
        }));

        Assert.Null((await db.Venues.FindAsync(1))!.EmailAddress);
    }

    [Fact]
    public async Task GetByIdAsync_ExposesContactFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ExposesContactFields));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            PhoneNumber = "+1 555 0100",
            EmailAddress = "hi@example.com",
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("+1 555 0100", result.PhoneNumber);
        Assert.Equal("hi@example.com", result.EmailAddress);
    }

    [Fact]
    public async Task CreateAsync_PersistsContactFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(CreateAsync_PersistsContactFields));
        var svc = CreateService(db);

        VenueDto result = await svc.CreateAsync(new VenueDto
        {
            Name = "New",
            PhoneNumber = " +1 555 0100 ",
            EmailAddress = " new@example.com ",
        });

        Assert.Equal("+1 555 0100", result.PhoneNumber);
        Assert.Equal("new@example.com", result.EmailAddress);
    }

    [Fact]
    public async Task UpdateAsync_Keeps_Description_WhenNull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Keeps_Description_WhenNull));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", Description = "Persisted blurb" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        // No Description on the request → PATCH leaves it untouched.
        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R2" });

        Assert.NotNull(result);
        Assert.Equal("Persisted blurb", result.Description);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Description()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_Returns_Description));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            Timezone = "UTC",
            Description = "Our little place"
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("Our little place", result.Description);
    }

    // ── Per-day opening hours ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_StoresPerDayHours_AndReturnsResolvedWeek()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_StoresPerDayHours_AndReturnsResolvedWeek));
        db.Venues.Add(new Venue { Id = 1, Name = "R", OpenTime = "09:00", CloseTime = "22:00", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var hours = Enumerable.Range(1, 7)
            .Select(d => new DayHoursDto { Day = d, Open = "10:00", Close = "20:00" })
            .ToList();
        hours[6] = new DayHoursDto { Day = 7, Open = "12:00", Close = "16:00" }; // Sunday differs

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", OpenHours = hours });

        Assert.NotNull(result);
        Assert.Equal(7, result!.OpenHours.Count);
        Assert.Equal("12:00", result.OpenHours.Single(h => h.Day == 7).Open);
        Assert.Equal("10:00", result.OpenHours.Single(h => h.Day == 1).Open);
        Assert.NotNull(db.Venues.Single(r => r.Id == 1).OpenHoursJson);
    }

    [Fact]
    public async Task UpdateAsync_CollapsesUniformPerDayHours_IntoOpenCloseTime()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_CollapsesUniformPerDayHours_IntoOpenCloseTime));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            OpenTime = "09:00",
            CloseTime = "22:00",
            Timezone = "UTC",
            OpenHoursJson = """{"6":{"open":"11:00","close":"23:00"}}"""
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var uniform = Enumerable.Range(1, 7)
            .Select(d => new DayHoursDto { Day = d, Open = "08:00", Close = "18:00" })
            .ToList();

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", OpenHours = uniform });

        Venue saved = db.Venues.Single(r => r.Id == 1);
        Assert.Null(saved.OpenHoursJson);
        Assert.Equal("08:00", saved.OpenTime);
        Assert.Equal("18:00", saved.CloseTime);
        Assert.All(result!.OpenHours, h =>
        {
            Assert.Equal("08:00", h.Open);
            Assert.Equal("18:00", h.Close);
        });
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenOpenHoursInvalid()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Throws_WhenOpenHoursInvalid));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var invalid = new List<DayHoursDto> { new() { Day = 9, Open = "10:00", Close = "20:00" } };

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", OpenHours = invalid }));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsResolvedOpenHours()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsResolvedOpenHours));
        db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "R",
            OpenTime = "09:00",
            CloseTime = "22:00",
            Timezone = "UTC",
            OpenHoursJson = """{"6":{"open":"11:00","close":"23:00"}}"""
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? dto = await svc.GetByIdAsync(1);

        Assert.NotNull(dto);
        Assert.Equal(7, dto!.OpenHours.Count);
        Assert.Equal("11:00", dto.OpenHours.Single(h => h.Day == 6).Open);
        Assert.Equal("09:00", dto.OpenHours.Single(h => h.Day == 1).Open);
    }

    // ── DefaultBookingDurationMinutes ────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsDefaultBookingDurationMinutes()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsDefaultBookingDurationMinutes));
        db.Venues.Add(new Venue { Id = 1, Name = "R", DefaultBookingDurationMinutes = 90 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);
        Assert.Equal(90, result!.DefaultBookingDurationMinutes);
    }

    [Fact]
    public async Task VenueDto_DefaultsBookingDurationTo60()
    {
        var dto = new VenueDto();
        Assert.Equal(60, dto.DefaultBookingDurationMinutes);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDefaultBookingDurationMinutes()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_UpdatesDefaultBookingDurationMinutes));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", DefaultBookingDurationMinutes = 120 });

        Assert.Equal(120, result!.DefaultBookingDurationMinutes);
        Venue? entity = await db.Venues.FindAsync(1);
        Assert.Equal(120, entity!.DefaultBookingDurationMinutes);
    }

    [Fact]
    public async Task UpdateAsync_KeepsExistingDuration_WhenNotProvided()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_KeepsExistingDuration_WhenNotProvided));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", DefaultBookingDurationMinutes = 90 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R" });

        Assert.Equal(90, result!.DefaultBookingDurationMinutes);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(500)]
    public async Task UpdateAsync_Throws_WhenDurationNotInAllowedSet(int invalidDuration)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_Throws_WhenDurationNotInAllowedSet)}_{invalidDuration}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", DefaultBookingDurationMinutes = invalidDuration }));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(480)]
    public async Task UpdateAsync_Accepts_WhenDurationInAllowedSet(int validDuration)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_Accepts_WhenDurationInAllowedSet)}_{validDuration}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", DefaultBookingDurationMinutes = validDuration });

        Assert.Equal(validDuration, result!.DefaultBookingDurationMinutes);
    }

    // ── BookingSlotIntervalMinutes ───────────────────────────────────────────
    // Mirrors the DefaultBookingDurationMinutes coverage above: the interval is a separate
    // venue-level setting (the step between selectable start times), decoupled from the
    // booking duration. Defaults to 30 and is constrained to {15, 30, 60} server-side.

    [Fact]
    public async Task GetByIdAsync_ReturnsBookingSlotIntervalMinutes()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsBookingSlotIntervalMinutes));
        db.Venues.Add(new Venue { Id = 1, Name = "R", BookingSlotIntervalMinutes = 15 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);
        Assert.Equal(15, result!.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task VenueDto_DefaultsBookingSlotIntervalTo30()
    {
        var dto = new VenueDto();
        Assert.Equal(30, dto.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesBookingSlotIntervalMinutes()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_UpdatesBookingSlotIntervalMinutes));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", BookingSlotIntervalMinutes = 15 });

        Assert.Equal(15, result!.BookingSlotIntervalMinutes);
        Venue? entity = await db.Venues.FindAsync(1);
        Assert.Equal(15, entity!.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task UpdateAsync_KeepsExistingInterval_WhenNotProvided()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_KeepsExistingInterval_WhenNotProvided));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC", BookingSlotIntervalMinutes = 15 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R" });

        Assert.Equal(15, result!.BookingSlotIntervalMinutes);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(0)]
    [InlineData(-15)]
    [InlineData(120)]
    public async Task UpdateAsync_Throws_WhenIntervalNotInAllowedSet(int invalidInterval)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_Throws_WhenIntervalNotInAllowedSet)}_{invalidInterval}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", BookingSlotIntervalMinutes = invalidInterval }));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task UpdateAsync_Accepts_WhenIntervalInAllowedSet(int validInterval)
    {
        using AppDbContext db = TestDbFactory.Create($"{nameof(UpdateAsync_Accepts_WhenIntervalInAllowedSet)}_{validInterval}");
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "R", BookingSlotIntervalMinutes = validInterval });

        Assert.Equal(validInterval, result!.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task UpdateAsync_DurationAndInterval_CanDiffer()
    {
        // The point of a separate interval: a 90-minute booking duration with a 15-minute start-time
        // interval must round-trip without either value rejecting the other.
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_DurationAndInterval_CanDiffer));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "R",
            DefaultBookingDurationMinutes = 90,
            BookingSlotIntervalMinutes = 15
        });

        Assert.Equal(90, result!.DefaultBookingDurationMinutes);
        Assert.Equal(15, result.BookingSlotIntervalMinutes);
    }

    [Fact]
    public async Task AddSectionAsync_ReturnsNull_WhenVenueNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddSectionAsync_ReturnsNull_WhenVenueNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.AddSectionAsync(999, "S1"));
    }

    [Fact]
    public async Task UpdateSectionAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateSectionAsync_ReturnsNull_WhenNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.UpdateSectionAsync(1, 1, "New"));
    }

    [Fact]
    public async Task DeleteSectionAsync_ReturnsFalse_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteSectionAsync_ReturnsFalse_WhenNotFound));
        var svc = CreateService(db);
        Assert.False(await svc.DeleteSectionAsync(1, 1));
    }

    [Fact]
    public async Task AddResourceAsync_ReturnsNull_WhenSectionNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceAsync_ReturnsNull_WhenSectionNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.AddResourceAsync(1, 1, "T1", 4));
    }

    [Fact]
    public async Task UpdateResourceAsync_ReturnsNull_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceAsync_ReturnsNull_WhenNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.UpdateResourceAsync(1, 1, 1, "New", 2));
    }

    [Fact]
    public async Task DeleteResourceAsync_ReturnsFalse_WhenNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceAsync_ReturnsFalse_WhenNotFound));
        var svc = CreateService(db);
        Assert.False(await svc.DeleteResourceAsync(1, 1, 1));
    }

    [Fact]
    public async Task GetAllAsync_ExcludesArchivedVenues()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAllAsync_ExcludesArchivedVenues));
        db.Venues.Add(new Venue { Id = 1, Name = "Active" });
        db.Venues.Add(new Venue { Id = 2, Name = "Archived", IsArchived = true });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        List<VenueDto> result = await svc.GetAllAsync();
        Assert.Single(result);
        Assert.Equal("Active", result[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsDto_WhenFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsDto_WhenFound));
        db.Venues.Add(new Venue { Id = 1, Name = "Found", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);
        Assert.NotNull(result);
        Assert.Equal("Found", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsConfiguredWalkInDays_WhenSet()
    {
        // WalkInDays defaults to null on the entity — ToDto's `r.WalkInDays ?? ""` fallback is
        // otherwise only ever exercised via the null branch across this file's other tests.
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsConfiguredWalkInDays_WhenSet));
        db.Venues.Add(new Venue { Id = 1, Name = "Found", Timezone = "UTC", WalkInDays = "6,7" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.Equal("6,7", result!.WalkInDays);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenArchived()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenArchived));
        db.Venues.Add(new Venue { Id = 1, Name = "Archived", IsArchived = true });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        Assert.Null(await svc.GetByIdAsync(1));
    }

    [Fact]
    public async Task UpdateAsync_SplitsTags()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_SplitsTags));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Tags = "Projector, Accessible, Quiet" });
        Assert.Equal(3, result!.Tags.Length);
        Assert.Contains("Projector", result.Tags);
    }

    [Fact]
    public async Task UpdateAsync_HandlesEmptyTags()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_HandlesEmptyTags));
        db.Venues.Add(new Venue { Id = 1, Name = "R", Tags = "old", Timezone = "UTC" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        VenueDto? result = await svc.UpdateAsync(1, new UpdateVenueRequest { Tags = "" });
        Assert.Empty(result!.Tags);
    }

    [Fact]
    public async Task AddSectionAsync_ReturnsSection_WhenVenueExists()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddSectionAsync_ReturnsSection_WhenVenueExists));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        SectionDto? result = await svc.AddSectionAsync(1, "Annex");
        Assert.NotNull(result);
        Assert.Equal("Annex", result.Name);
        Assert.Empty(result.Resources);
    }

    [Fact]
    public async Task UpdateSectionAsync_UpdatesName_WhenFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateSectionAsync_UpdatesName_WhenFound));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "Old", VenueId = 1 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        SectionDto? result = await svc.UpdateSectionAsync(1, 1, "New");
        Assert.NotNull(result);
        Assert.Equal("New", result.Name);
    }

    [Fact]
    public async Task DeleteSectionAsync_ReturnsTrue_AndNullsBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteSectionAsync_ReturnsTrue_AndNullsBookings));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = DateTime.UtcNow, BookingRef = "REF1" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        bool result = await svc.DeleteSectionAsync(1, 1);

        Assert.True(result);
        Booking? booking = await db.Bookings.FindAsync(1);
        Assert.Null(booking!.ResourceId);
        Assert.Null(booking.SectionId);
        Assert.False(await db.Sections.AnyAsync(s => s.Id == 1));
    }

    [Fact]
    public async Task AddResourceAsync_ReturnsResource_WhenSectionExists()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceAsync_ReturnsResource_WhenSectionExists));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        ResourceDto? result = await svc.AddResourceAsync(1, 1, "T1", 4);
        Assert.NotNull(result);
        Assert.Equal("T1", result.Name);
        Assert.Equal(4, result.Capacity);
    }

    [Fact]
    public async Task UpdateResourceAsync_TogglesWalkInOnly()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceAsync_TogglesWalkInOnly));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        ResourceDto added = (await svc.AddResourceAsync(1, 1, "Walk-in", 2, walkInOnly: true))!;

        ResourceDto? released = await svc.UpdateResourceAsync(1, 1, added.Id, "Window", 2, walkInOnly: false);

        Assert.True(added.WalkInOnly);
        Assert.False(released!.WalkInOnly);
        Assert.False(db.Resources.Single().WalkInOnly);
    }

    [Fact]
    public async Task UpdateResourceAsync_UpdatesNameAndCapacity_WhenFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceAsync_UpdatesNameAndCapacity_WhenFound));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "Old", Capacity = 2, SectionId = 1 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        ResourceDto? result = await svc.UpdateResourceAsync(1, 1, 1, "New", 6);
        Assert.NotNull(result);
        Assert.Equal("New", result.Name);
        Assert.Equal(6, result.Capacity);
    }

    [Fact]
    public async Task DeleteResourceAsync_ReturnsTrue_AndNullsBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceAsync_ReturnsTrue_AndNullsBookings));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = DateTime.UtcNow, BookingRef = "REF1" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        bool result = await svc.DeleteResourceAsync(1, 1, 1);

        Assert.True(result);
        Booking? booking = await db.Bookings.FindAsync(1);
        Assert.Null(booking!.ResourceId);
        Assert.False(await db.Resources.AnyAsync(t => t.Id == 1));
    }

    // ── Delete-impact reads (two-step delete friction) ──────────────────────
    //
    // The impact count is the number shown to the admin in the inline confirm step — it must count
    // only non-cancelled future bookings (those a live venue cares about losing the reference
    // for), and null out when the resource/section doesn't exist or belongs to another venue.

    [Fact]
    public async Task GetResourceDeleteImpactAsync_ReturnsNull_WhenResourceNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetResourceDeleteImpactAsync_ReturnsNull_WhenResourceNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.GetResourceDeleteImpactAsync(1, 1, 1));
    }

    [Fact]
    public async Task GetResourceDeleteImpactAsync_CountsFutureNonCancelledBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetResourceDeleteImpactAsync_CountsFutureNonCancelledBookings));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        DateTime now = DateTime.UtcNow;
        // 2 future + active on this resource → counted.
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(1), BookingRef = "F1" });
        db.Bookings.Add(new Booking { Id = 2, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddDays(2), BookingRef = "F2" });
        // 1 future but cancelled → excluded.
        db.Bookings.Add(new Booking { Id = 3, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(3), BookingRef = "C1", IsCancelled = true });
        // 1 past + active → excluded (the booking already happened; losing the ref is moot).
        db.Bookings.Add(new Booking { Id = 4, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(-2), BookingRef = "P1" });
        // 1 future on a different resource → excluded.
        db.Resources.Add(new Resource { Id = 2, Capacity = 4, SectionId = 1 });
        db.Bookings.Add(new Booking { Id = 5, VenueId = 1, ResourceId = 2, SectionId = 1, Date = now.AddHours(4), BookingRef = "OTHER" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        DeleteImpactDto? result = await svc.GetResourceDeleteImpactAsync(1, 1, 1);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Bookings);
    }

    [Fact]
    public async Task GetResourceDeleteImpactAsync_ReturnsZero_WhenNoFutureBookings()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetResourceDeleteImpactAsync_ReturnsZero_WhenNoFutureBookings));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        DeleteImpactDto? result = await svc.GetResourceDeleteImpactAsync(1, 1, 1);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Bookings);
    }

    [Fact]
    public async Task GetSectionDeleteImpactAsync_ReturnsNull_WhenSectionNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetSectionDeleteImpactAsync_ReturnsNull_WhenSectionNotFound));
        var svc = CreateService(db);
        Assert.Null(await svc.GetSectionDeleteImpactAsync(1, 1));
    }

    [Fact]
    public async Task GetSectionDeleteImpactAsync_CountsFutureNonCancelledAcrossSectionAndItsResources()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetSectionDeleteImpactAsync_CountsFutureNonCancelledAcrossSectionAndItsResources));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Capacity = 4, SectionId = 1 });
        DateTime now = DateTime.UtcNow;
        // Future booking matched by ResourceId (resource 1) → counted.
        db.Bookings.Add(new Booking { Id = 1, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(1), BookingRef = "BT" });
        // Future booking matched by SectionId only (resource ref already null, e.g. walk-in assigned to section) → counted.
        db.Bookings.Add(new Booking { Id = 2, VenueId = 1, ResourceId = null, SectionId = 1, Date = now.AddHours(2), BookingRef = "BS" });
        // Future booking on resource 2 → counted.
        db.Bookings.Add(new Booking { Id = 3, VenueId = 1, ResourceId = 2, SectionId = 1, Date = now.AddHours(3), BookingRef = "BT2" });
        // Cancelled future on resource 1 → excluded.
        db.Bookings.Add(new Booking { Id = 4, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(4), BookingRef = "CX", IsCancelled = true });
        // Past active on resource 1 → excluded.
        db.Bookings.Add(new Booking { Id = 5, VenueId = 1, ResourceId = 1, SectionId = 1, Date = now.AddHours(-1), BookingRef = "PAST" });
        // Future on a different section → excluded.
        db.Sections.Add(new Section { Id = 2, Name = "Other", VenueId = 1 });
        db.Bookings.Add(new Booking { Id = 6, VenueId = 1, ResourceId = null, SectionId = 2, Date = now.AddHours(5), BookingRef = "OTHERSEC" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        DeleteImpactDto? result = await svc.GetSectionDeleteImpactAsync(1, 1);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Bookings);
    }

    // ── SortOrder / reorderable sections ─────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_OrdersSectionsBySortOrder_ThenById()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_OrdersSectionsBySortOrder_ThenById));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        // Inserted out of SortOrder order, and out of alphabetical order too, to prove
        // neither insertion order nor name drives the result.
        db.Sections.Add(new Section { Id = 1, Name = "Zebra", VenueId = 1, SortOrder = 2 });
        db.Sections.Add(new Section { Id = 2, Name = "Alpha", VenueId = 1, SortOrder = 0 });
        db.Sections.Add(new Section { Id = 3, Name = "Middle", VenueId = 1, SortOrder = 1 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal(["Alpha", "Middle", "Zebra"], result!.Sections.Select(s => s.Name));
    }

    [Fact]
    public async Task GetByIdAsync_OrdersBySections_TieBreaksById_WhenSortOrderEqual()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_OrdersBySections_TieBreaksById_WhenSortOrderEqual));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 2, Name = "Second", VenueId = 1, SortOrder = 0 });
        db.Sections.Add(new Section { Id = 1, Name = "First", VenueId = 1, SortOrder = 0 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.Equal(["First", "Second"], result!.Sections.Select(s => s.Name));
    }

    [Fact]
    public async Task AddSectionAsync_AppendsSortOrder_AtEndOfExistingSections()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddSectionAsync_AppendsSortOrder_AtEndOfExistingSections));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "Existing1", VenueId = 1, SortOrder = 0 });
        db.Sections.Add(new Section { Id = 2, Name = "Existing2", VenueId = 1, SortOrder = 1 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        SectionDto? result = await svc.AddSectionAsync(1, "New");

        Assert.NotNull(result);
        Assert.Equal(2, result!.SortOrder);
        Section saved = await db.Sections.SingleAsync(s => s.Name == "New");
        Assert.Equal(2, saved.SortOrder);
    }

    [Fact]
    public async Task AddSectionAsync_FirstSection_GetsSortOrderZero()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddSectionAsync_FirstSection_GetsSortOrderZero));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        SectionDto? result = await svc.AddSectionAsync(1, "Only");

        Assert.NotNull(result);
        Assert.Equal(0, result!.SortOrder);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEmptySections_WhenVenueHasNone()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsEmptySections_WhenVenueHasNone));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Empty(result!.Sections);
    }

    [Fact]
    public async Task GetByIdAsync_OrdersSingleSection_WithoutError()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_OrdersSingleSection_WithoutError));
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "Only", VenueId = 1, SortOrder = 0 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal(["Only"], result!.Sections.Select(s => s.Name));
    }

    // ── Combinable resource groups ─────────────────────────────────────────────
    //
    // A group is a bookable unit of combined physical resources. Service enforces every data-
    // integrity rule (in-memory provider used here ignores SQL constraints, so the unique index on
    // ResourceId is the production backstop): members exist + belong to the same venue, aren't
    // already grouped, >= 2 members, and CombinedCapacity inside (largest member .. sum of members].

    private static async Task SeedTwoVenuesWithResourcesAsync(AppDbContext db)
    {
        // Venue 1 — section 1 with resources 1, 2, 3.
        db.Venues.Add(new Venue { Id = 1, Name = "R1" });
        db.Sections.Add(new Section { Id = 1, Name = "S1", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 3, Capacity = 2, SectionId = 1 });
        // Venue 2 — section 2 with resource 4.
        db.Venues.Add(new Venue { Id = 2, Name = "R2" });
        db.Sections.Add(new Section { Id = 2, Name = "S2", VenueId = 2 });
        db.Resources.Add(new Resource { Id = 4, Capacity = 6, SectionId = 2 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AddResourceGroupAsync_ReturnsNull_WhenVenueNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_ReturnsNull_WhenVenueNotFound));
        var svc = CreateService(db);
        ResourceGroupDto? result = await svc.AddResourceGroupAsync(999, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });
        Assert.Null(result);
    }

    [Fact]
    public async Task AddResourceGroupAsync_CreatesGroup_FromTwoResources()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_CreatesGroup_FromTwoResources));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        ResourceGroupDto? result = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Name = "Window desks",
            Members = [1, 2],
            CombinedCapacity = 8
        });

        Assert.NotNull(result);
        Assert.Equal("Window desks", result!.Name);
        Assert.Equal(8, result.CombinedCapacity);
        Assert.Equal([1, 2], result.Members.Select(m => m.Id));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsFewerThanTwoMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsFewerThanTwoMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1], CombinedCapacity = 4 }));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsDuplicateMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsDuplicateMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 1], CombinedCapacity = 4 }));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsMemberFromDifferentVenue()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsMemberFromDifferentVenue));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        // Resource 4 belongs to venue 2 — must be rejected when grouping under venue 1.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 4], CombinedCapacity = 10 }));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsResourceAlreadyInAnotherGroup()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsResourceAlreadyInAnotherGroup));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });

        // Resource 1 is now grouped — a second group including it must be rejected.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 3], CombinedCapacity = 6 }));
    }

    [Fact]
    public async Task AddResourceGroupAsync_AllowsCombinedCapacityBelowSumOfMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_AllowsCombinedCapacityBelowSumOfMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        // Resources 1+2 fit 4+4 = 8, but combining them loses the places where the corners
        // meet — 7 (or 6) is the realistic combined figure and must be accepted.
        ResourceGroupDto? result = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Members = [1, 2],
            CombinedCapacity = 7
        });
        Assert.NotNull(result);
        Assert.Equal(7, result!.CombinedCapacity);
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsCombinedCapacityAboveSumOfMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsCombinedCapacityAboveSumOfMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        // Combining resources cannot invent places: 10 > 4 + 4.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 10 }));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsCombinedCapacityNotBeatingLargestMember()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsCombinedCapacityNotBeatingLargestMember));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        // Resources 1 (capacity 4) + 3 (capacity 2): a combined figure of 4 fits no more than resource 1 does
        // on its own, so the group would be pointless — and would shadow resource 1 in the ordering.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 3], CombinedCapacity = 4 }));
    }

    [Fact]
    public async Task UpdateResourceGroupAsync_RenamesAndSwapsMembersAndCombinedCapacity()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceGroupAsync_RenamesAndSwapsMembersAndCombinedCapacity));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        ResourceGroupDto? created = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Name = "Old",
            Members = [1, 2],
            CombinedCapacity = 8
        });

        // Swap resource 2 out for resource 3, rename, set combined capacity to the new sum (4+2).
        ResourceGroupDto? updated = await svc.UpdateResourceGroupAsync(1, created!.Id, new UpdateResourceGroupRequest
        {
            Name = "New",
            Members = [1, 3],
            CombinedCapacity = 6
        });

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
        Assert.Equal(6, updated.CombinedCapacity);
        Assert.Equal([1, 3], updated.Members.Select(m => m.Id));

        // The removed resource (2) is now ungrouped, so it can form a new group with another free resource.
        // Resource 3 is taken by the group just updated, so this must instead reject.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [2, 3], CombinedCapacity = 6 }));
    }

    [Fact]
    public async Task UpdateResourceGroupAsync_ReturnsNull_WhenGroupNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceGroupAsync_ReturnsNull_WhenGroupNotFound));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        ResourceGroupDto? result = await svc.UpdateResourceGroupAsync(1, 999, new UpdateResourceGroupRequest
        {
            Members = [1, 2],
            CombinedCapacity = 8
        });
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateResourceGroupAsync_AllowsKeepingOwnMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceGroupAsync_AllowsKeepingOwnMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        ResourceGroupDto? created = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Members = [1, 2],
            CombinedCapacity = 8
        });

        // Re-submitting the same members (a rename-only update) must not trip the "already grouped" rule.
        ResourceGroupDto? updated = await svc.UpdateResourceGroupAsync(1, created!.Id, new UpdateResourceGroupRequest
        {
            Name = "Renamed",
            Members = [1, 2],
            CombinedCapacity = 8
        });

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Name);
    }

    [Fact]
    public async Task DeleteResourceGroupAsync_ReturnsFalse_WhenGroupNotFound()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceGroupAsync_ReturnsFalse_WhenGroupNotFound));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        Assert.False(await svc.DeleteResourceGroupAsync(1, 999));
    }

    [Fact]
    public async Task DeleteResourceGroupAsync_RemovesGroupAndClearsBookingReferences()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceGroupAsync_RemovesGroupAndClearsBookingReferences));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        ResourceGroupDto? group = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Members = [1, 2],
            CombinedCapacity = 8
        });
        // A booking referencing the group.
        db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            ResourceGroupId = group!.Id,
            Date = DateTime.UtcNow.AddDays(1),
            BookingRef = "G1"
        });
        await db.SaveChangesAsync();

        bool result = await svc.DeleteResourceGroupAsync(1, group.Id);

        Assert.True(result);
        Booking? booking = await db.Bookings.FindAsync(1);
        Assert.Null(booking!.ResourceGroupId); // FK-null equivalent
        Assert.False(await db.ResourceGroups.AnyAsync(g => g.Id == group.Id));
        Assert.False(await db.ResourceGroupMemberships.AnyAsync(m => m.ResourceGroupId == group.Id));
    }

    [Fact]
    public async Task GetByIdAsync_IncludesGroupsWithMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_IncludesGroupsWithMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
        {
            Name = "Desks",
            Members = [1, 2],
            CombinedCapacity = 8
        });

        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        ResourceGroupDto group = Assert.Single(result!.Groups);
        Assert.Equal("Desks", group.Name);
        Assert.Equal(8, group.CombinedCapacity);
        Assert.Equal([1, 2], group.Members.Select(m => m.Id));
    }

    [Fact]
    public async Task GetAllAsync_IncludesGroupsWithMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetAllAsync_IncludesGroupsWithMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 3], CombinedCapacity = 6 });

        List<VenueDto> result = await svc.GetAllAsync();

        VenueDto r1 = result.Single(r => r.Id == 1);
        ResourceGroupDto group = Assert.Single(r1.Groups);
        Assert.Equal([1, 3], group.Members.Select(m => m.Id));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEmptyGroups_WhenNoneDefined()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ReturnsEmptyGroups_WhenNoneDefined));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        VenueDto? result = await svc.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Empty(result!.Groups);
    }

    // ── Capacity validation (defense in depth behind the DTO [Range] annotations) ───────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BookingLimits.MaxPartySize + 1)]
    public async Task AddResourceAsync_RejectsCapacityOutOfRange(int capacity)
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceAsync_RejectsCapacityOutOfRange) + capacity);
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.AddResourceAsync(1, 1, "T1", capacity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(BookingLimits.MaxPartySize + 1)]
    public async Task UpdateResourceAsync_RejectsCapacityOutOfRange(int capacity)
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceAsync_RejectsCapacityOutOfRange) + capacity);
        db.Venues.Add(new Venue { Id = 1, Name = "R" });
        db.Sections.Add(new Section { Id = 1, Name = "S", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateResourceAsync(1, 1, 1, "T1", capacity));
    }

    [Fact]
    public async Task AddResourceGroupAsync_RejectsCombinedCapacityAboveMax()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(AddResourceGroupAsync_RejectsCombinedCapacityAboveMax));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest
            {
                Members = [1, 2],
                CombinedCapacity = BookingLimits.MaxPartySize + 1
            }));
    }

    // ── Group integrity when member resources change underneath ────────────────
    //
    // The ResourceGroupMemberships → Resources FK is ON DELETE CASCADE, so a member resource's removal drops
    // the membership row without touching the group. Left alone, the group keeps advertising a
    // combined capacity nothing can offer.

    [Fact]
    public async Task DeleteResourceAsync_DissolvesGroup_WhenItWouldDropBelowTwoMembers()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceAsync_DissolvesGroup_WhenItWouldDropBelowTwoMembers));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        ResourceGroupDto? group = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });

        Assert.True(await svc.DeleteResourceAsync(1, 1, 2));

        VenueDto? venue = await svc.GetByIdAsync(1);
        Assert.Empty(venue!.Groups);
        Assert.Null(await db.ResourceGroups.FindAsync(group!.Id));
    }

    [Fact]
    public async Task DeleteResourceAsync_FkNullsGroupBookings_WhenTheGroupIsDissolved()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceAsync_FkNullsGroupBookings_WhenTheGroupIsDissolved));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        ResourceGroupDto? group = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });
        db.Bookings.Add(new Booking
        {
            Id = 90, VenueId = 1, ResourceGroupId = group!.Id, SectionId = 1,
            Date = DateTime.UtcNow.AddDays(3), BookingRef = "GRP1"
        });
        await db.SaveChangesAsync();

        Assert.True(await svc.DeleteResourceAsync(1, 1, 2));

        Booking? booking = await db.Bookings.FindAsync(90);
        Assert.NotNull(booking);
        Assert.Null(booking!.ResourceGroupId);
    }

    [Fact]
    public async Task DeleteResourceAsync_ClampsCombinedCapacity_WhenTheGroupSurvives()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteResourceAsync_ClampsCombinedCapacity_WhenTheGroupSurvives));
        db.Venues.Add(new Venue { Id = 1, Name = "R1" });
        db.Sections.Add(new Section { Id = 1, Name = "S1", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 3, Capacity = 4, SectionId = 1 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2, 3], CombinedCapacity = 12 });

        Assert.True(await svc.DeleteResourceAsync(1, 1, 3));

        VenueDto? venue = await svc.GetByIdAsync(1);
        ResourceGroupDto group = Assert.Single(venue!.Groups);
        Assert.Equal([1, 2], group.Members.Select(m => m.Id));
        Assert.Equal(8, group.CombinedCapacity);
    }

    [Fact]
    public async Task DeleteSectionAsync_DissolvesGroups_SpanningItsResources()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(DeleteSectionAsync_DissolvesGroups_SpanningItsResources));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });

        Assert.True(await svc.DeleteSectionAsync(1, 1));

        Assert.Empty(db.ResourceGroups.ToList());
    }

    [Fact]
    public async Task UpdateResourceAsync_ClampsCombinedCapacity_WhenAMemberShrinks()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateResourceAsync_ClampsCombinedCapacity_WhenAMemberShrinks));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });

        // Resource 2 drops from capacity 4 to 2 — the pair can no longer fit 8.
        Assert.NotNull(await svc.UpdateResourceAsync(1, 1, 2, "T2", 2));

        VenueDto? venue = await svc.GetByIdAsync(1);
        ResourceGroupDto group = Assert.Single(venue!.Groups);
        Assert.Equal(6, group.CombinedCapacity);
    }

    [Fact]
    public async Task GetResourceDeleteImpactAsync_CountsBookingsHeldThroughTheResourcesGroup()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetResourceDeleteImpactAsync_CountsBookingsHeldThroughTheResourcesGroup));
        await SeedTwoVenuesWithResourcesAsync(db);
        var svc = CreateService(db);
        ResourceGroupDto? group = await svc.AddResourceGroupAsync(1, new CreateResourceGroupRequest { Members = [1, 2], CombinedCapacity = 8 });
        // A group booking stores ResourceId = null, so the resource-only count would report zero impact.
        db.Bookings.Add(new Booking
        {
            Id = 91, VenueId = 1, ResourceGroupId = group!.Id, SectionId = 1,
            Date = DateTime.UtcNow.AddDays(4), BookingRef = "GRP2"
        });
        await db.SaveChangesAsync();

        DeleteImpactDto? impact = await svc.GetResourceDeleteImpactAsync(1, 1, 1);

        Assert.Equal(1, impact!.Bookings);
    }
}
