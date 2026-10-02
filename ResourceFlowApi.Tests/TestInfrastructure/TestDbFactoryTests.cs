using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.TestInfrastructure;

/// <summary>
/// Tests for the test infrastructure helpers themselves. They become load-bearing
/// once ~14 service test files depend on them, so they warrant their own coverage.
/// </summary>
public class TestDbFactoryTests
{
    [Fact]
    public void Create_ReturnsUsableDbContext()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(Create_ReturnsUsableDbContext));

        // A writable DbSet round-trip proves the context is functional.
        db.Venues.Add(new Venue { Id = 1, Name = "X" });
        db.SaveChanges();

        Assert.Equal(1, db.Venues.Count());
    }

    [Fact]
    public void Create_WithDistinctNames_YieldsIsolatedDatabases()
    {
        // Two distinct names must not share state — the whole point of per-test naming.
        using AppDbContext dbA = TestDbFactory.Create(nameof(Create_WithDistinctNames_YieldsIsolatedDatabases) + "_A");
        using AppDbContext dbB = TestDbFactory.Create(nameof(Create_WithDistinctNames_YieldsIsolatedDatabases) + "_B");

        dbA.Venues.Add(new Venue { Id = 1, Name = "A" });
        dbA.SaveChanges();

        Assert.Single(dbA.Venues);
        Assert.Empty(dbB.Venues);
    }

    [Fact]
    public void BasicVenue_SeedsSingleResourceGraph()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(BasicVenue_SeedsSingleResourceGraph));

        TestSeed.BasicVenue(db);

        Assert.Single(db.Venues);
        Assert.Single(db.Sections);
        Resource resource = Assert.Single(db.Resources);
        Assert.Equal(4, resource.Capacity);
        // BasicVenue doesn't override opening hours — the Venue entity's
        // own defaults ("09:00"/"22:00") apply, distinguishing it from VenueWithHours.
        Assert.Equal("09:00", db.Venues.First().OpenTime);
    }

    [Fact]
    public void VenueWithHours_SeedsTwoResourcesAndOpeningHours()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(VenueWithHours_SeedsTwoResourcesAndOpeningHours));

        TestSeed.VenueWithHours(db);

        Venue r = db.Venues.First();
        Assert.Equal("11:00", r.OpenTime);
        Assert.Equal("13:00", r.CloseTime);
        Assert.Equal("UTC", r.Timezone);
        Assert.Equal(2, db.Resources.Count());
    }

    [Fact]
    public void VenueWithHours_AcceptsCustomHours()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(VenueWithHours_AcceptsCustomHours));

        TestSeed.VenueWithHours(db, open: "09:00", close: "22:00", tz: "America/New_York");

        Venue r = db.Venues.First();
        Assert.Equal("09:00", r.OpenTime);
        Assert.Equal("22:00", r.CloseTime);
        Assert.Equal("America/New_York", r.Timezone);
    }
}
