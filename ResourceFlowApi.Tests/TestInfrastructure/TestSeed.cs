using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.TestInfrastructure;

/// <summary>
/// Seeds the canonical Venue+Section+Resource graph used across service tests.
/// Variant overloads match the two seeding shapes observed in the codebase.
/// </summary>
internal static class TestSeed
{
    /// <summary>
    /// Single 4-place resource (BookingService shape). Venue has no opening hours set.
    /// </summary>
    public static void BasicVenue(AppDbContext db)
    {
        db.Venues.Add(new Venue { Id = 1, Name = "Test Venue" });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.SaveChanges();
    }

    /// <summary>
    /// Venue with OpenTime/CloseTime/Timezone set, plus two resources (Availability shape).
    /// Defaults to the 11:00-13:00 UTC window used by availability slot tests.
    /// </summary>
    public static void VenueWithHours(
        AppDbContext db,
        string open = "11:00",
        string close = "13:00",
        string tz = "UTC")
    {
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Test", OpenTime = open, CloseTime = close, Timezone = tz
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 2, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        db.SaveChanges();
    }
}
