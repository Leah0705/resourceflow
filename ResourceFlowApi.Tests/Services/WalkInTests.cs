using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// Walk-in policy tests for <see cref="WalkInHelper"/> and the admin update
/// path. Service-level rejection tests live in <see cref="BookingServiceTests"/>
/// and <see cref="AvailabilityServiceTests"/>, which are the classes allowed to
/// construct the restricted repository types.
/// </summary>
public class WalkInTests
{
    private static VenueManagementService CreateService(AppDbContext db) => new(
        new VenueRepository(db),
        new SectionRepository(db),
        new ResourceRepository(db),
        new BookingRepository(db),
        new ResourceGroupRepository(db));

    // ── WalkInHelper ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseWalkInDays_ReturnsEmpty_ForNullOrBlank(string? input)
    {
        Assert.Empty(WalkInHelper.ParseWalkInDays(input));
    }

    [Fact]
    public void ParseWalkInDays_ParsesValidDays_AndIgnoresJunk()
    {
        HashSet<int> days = WalkInHelper.ParseWalkInDays(" 6 ,7,0,8,abc,6");
        Assert.True(days.SetEquals([6, 7]));
    }

    [Fact]
    public void NormalizeWalkInDays_SortsAndDeduplicates()
    {
        Assert.Equal("2,6,7", WalkInHelper.NormalizeWalkInDays("7, 2,6,2"));
    }

    [Fact]
    public void NormalizeWalkInDays_ReturnsNull_WhenEmpty()
    {
        Assert.Null(WalkInHelper.NormalizeWalkInDays(""));
        Assert.Null(WalkInHelper.NormalizeWalkInDays(" , "));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("8")]
    [InlineData("monday")]
    public void NormalizeWalkInDays_Throws_ForInvalidEntries(string input)
    {
        Assert.Throws<ValidationException>(() => WalkInHelper.NormalizeWalkInDays(input));
    }

    [Fact]
    public void IsWalkInOnlyAt_ReturnsTrue_WhenLocationIsWalkInOnly()
    {
        var r = new Venue { Name = "T", WalkInOnly = true };
        Assert.True(WalkInHelper.IsWalkInOnlyAt(r, DateTime.UtcNow));
    }

    [Fact]
    public void IsWalkInOnlyAt_ReturnsFalse_WhenNoWalkInDays()
    {
        var r = new Venue { Name = "T" };
        Assert.False(WalkInHelper.IsWalkInOnlyAt(r, DateTime.UtcNow));
    }

    [Fact]
    public void IsWalkInOnlyAt_UsesVenueTimezone_ForDayBoundary()
    {
        // Sunday 02:00 UTC is still Saturday evening in Los Angeles.
        var r = new Venue { Name = "T", Timezone = "America/Los_Angeles", WalkInDays = "6" };
        var sundayUtc = new DateTime(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

        Assert.True(WalkInHelper.IsWalkInOnlyAt(r, sundayUtc));
    }

    [Fact]
    public void IsWalkInOnlyAt_FallsBackToUtc_ForUnknownTimezone()
    {
        var r = new Venue { Name = "T", Timezone = "Not/AZone", WalkInDays = "7" };
        var sundayUtc = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(WalkInHelper.IsWalkInOnlyAt(r, sundayUtc));
    }

    [Fact]
    public void IsWalkInOnlyOn_MatchesDay()
    {
        var r = new Venue { Name = "T", WalkInDays = "6,7" };
        Assert.True(WalkInHelper.IsWalkInOnlyOn(r, 6));
        Assert.False(WalkInHelper.IsWalkInOnlyOn(r, 3));
    }

    // ── VenueManagementService ───────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SetsAndNormalizesWalkInFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_SetsAndNormalizesWalkInFields));
        db.Venues.Add(new Venue { Id = 1, Name = "T" });
        db.SaveChanges();

        var svc = CreateService(db);
        VenueDto? dto = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "T",
            WalkInOnly = true,
            WalkInDays = "7, 6"
        });

        Assert.NotNull(dto);
        Assert.True(dto.WalkInOnly);
        Assert.Equal("6,7", dto.WalkInDays);

        Venue? entity = await db.Venues.FindAsync(1);
        Assert.True(entity!.WalkInOnly);
        Assert.Equal("6,7", entity.WalkInDays);
    }

    [Fact]
    public async Task UpdateAsync_ClearsWalkInDays_WithEmptyString()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_ClearsWalkInDays_WithEmptyString));
        db.Venues.Add(new Venue { Id = 1, Name = "T", WalkInOnly = true, WalkInDays = "6,7" });
        db.SaveChanges();

        var svc = CreateService(db);
        VenueDto? dto = await svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "T",
            WalkInOnly = false,
            WalkInDays = ""
        });

        Assert.NotNull(dto);
        Assert.False(dto.WalkInOnly);
        Assert.Equal("", dto.WalkInDays);

        Venue? entity = await db.Venues.FindAsync(1);
        Assert.False(entity!.WalkInOnly);
        Assert.Null(entity.WalkInDays);
    }

    [Fact]
    public async Task UpdateAsync_Throws_ForInvalidWalkInDays()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_Throws_ForInvalidWalkInDays));
        db.Venues.Add(new Venue { Id = 1, Name = "T" });
        db.SaveChanges();

        var svc = CreateService(db);
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(1, new UpdateVenueRequest
        {
            Name = "T",
            WalkInDays = "8"
        }));
    }

    [Fact]
    public async Task UpdateAsync_LeavesWalkInFieldsUntouched_WhenOmitted()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(UpdateAsync_LeavesWalkInFieldsUntouched_WhenOmitted));
        db.Venues.Add(new Venue { Id = 1, Name = "T", WalkInOnly = true, WalkInDays = "6" });
        db.SaveChanges();

        var svc = CreateService(db);
        VenueDto? dto = await svc.UpdateAsync(1, new UpdateVenueRequest { Name = "T2" });

        Assert.NotNull(dto);
        Assert.True(dto.WalkInOnly);
        Assert.Equal("6", dto.WalkInDays);
    }

    [Fact]
    public async Task GetByIdAsync_ExposesWalkInFields()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(GetByIdAsync_ExposesWalkInFields));
        db.Venues.Add(new Venue { Id = 1, Name = "T", WalkInDays = "6,7" });
        db.SaveChanges();

        var svc = CreateService(db);
        VenueDto? dto = await svc.GetByIdAsync(1);

        Assert.NotNull(dto);
        Assert.False(dto.WalkInOnly);
        Assert.Equal("6,7", dto.WalkInDays);
    }
}
