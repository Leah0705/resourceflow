using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Holds;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

public class ResourceAutoAssignerTests
{
    private sealed class UtcClock : ISystemClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// Seeds a venue with two standalone 4-place resources (T1, T2) and a combinable group made of
    /// two 4-place resources (T3, T4) with CombinedCapacity 8. Used to exercise the deprioritization +
    /// group-candidate logic.
    /// </summary>
    private static (Venue, AppDbContext) SeedWithGroup(string name)
    {
        AppDbContext db = TestDbFactory.Create(name);
        db.Venues.Add(new Venue
        {
            Id = 1, Name = "Group Venue", OpenTime = "00:00", CloseTime = "23:59", Timezone = "UTC"
        });
        db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 2, Name = "T2", Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 3, Name = "T3", Capacity = 4, SectionId = 1 });
        db.Resources.Add(new Resource { Id = 4, Name = "T4", Capacity = 4, SectionId = 1 });
        db.ResourceGroups.Add(new ResourceGroup
        {
            Id = 1, VenueId = 1, CombinedCapacity = 8,
            Members = new List<ResourceGroupMembership>
            {
                new() { ResourceGroupId = 1, ResourceId = 3 },
                new() { ResourceGroupId = 1, ResourceId = 4 }
            }
        });
        db.SaveChanges();

        // Re-fetch the venue with its full graph (Sections → Resources + Groups → Members → Resource)
        // so the assigner can walk venue.Sections and venue.Groups. Memberships' .Resource nav
        // is populated by the Include (left null during seeding to avoid duplicate-tracking).
        Venue? loaded = db.Venues
            .Include(r => r.Sections).ThenInclude(s => s.Resources)
            .Include(r => r.Groups).ThenInclude(g => g.Members).ThenInclude(m => m.Resource)
            .First();
        return (loaded, db);
    }

    private static ResourceAutoAssigner CreateAssigner(AppDbContext db, IHoldService? holdService = null)
    {
        holdService ??= new HoldService(new UtcClock());
        return new ResourceAutoAssigner(new BookingRepository(db), holdService);
    }

    [Fact]
    public async Task BuildCandidates_DeprioritizesGroups_AgainstStandaloneResourcesOfEqualCapacity()
    {
        // Party of 4: both standalone resources (T1, T2) and the group (CombinedCapacity 8) fit. The
        // group has a larger capacity (8 > 4) so it sorts after by Capacity already; but the key
        // deprioritization rule is IsGroup as a tiebreaker — assert standalone resources come first.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_DeprioritizesGroups_AgainstStandaloneResourcesOfEqualCapacity));
        using (db)
        {
            ResourceAutoAssigner assigner = CreateAssigner(db);

            IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 4, DateTime.UtcNow.AddDays(10));

            // Standalone resources first, then the group. No standalone resource should appear after a group.
            int firstGroupIndex = candidates.TakeWhile(c => !c.IsGroup).Count();
            Assert.True(firstGroupIndex >= 2); // at least the two standalone resources precede any group
            Assert.Contains(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
        }
    }

    [Fact]
    public async Task BuildCandidates_SkipsWalkInOnlyResourcesAndGroups_Online()
    {
        // T1 is walk-in only, and so is T3, which takes its whole group (T3 + T4) with it.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_SkipsWalkInOnlyResourcesAndGroups_Online));
        using (db)
        {
            venue.Sections.Single().Resources.Where(t => t.Id is 1 or 3).ToList().ForEach(t => t.WalkInOnly = true);
            ResourceAutoAssigner assigner = CreateAssigner(db);

            IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 2, DateTime.UtcNow.AddDays(10));

            Assert.Equal([2, 4], candidates.Select(c => c.ResourceId));
            Assert.DoesNotContain(candidates, c => c.IsGroup);
        }
    }

    [Fact]
    public async Task BuildCandidates_OffersWalkInOnlyResourcesAndGroups_ToTheWaitlist()
    {
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_OffersWalkInOnlyResourcesAndGroups_ToTheWaitlist));
        using (db)
        {
            venue.Sections.Single().Resources.Where(t => t.Id is 1 or 3).ToList().ForEach(t => t.WalkInOnly = true);
            ResourceAutoAssigner assigner = CreateAssigner(db);

            IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(
                venue, partySize: 2, DateTime.UtcNow.AddDays(10), includeWalkInOnly: true);

            Assert.Contains(candidates, c => !c.IsGroup && c.ResourceId == 1);
            Assert.Contains(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
        }
    }

    [Fact]
    public async Task BuildCandidates_PrefersStandaloneResource_WhenPartyFitsBoth()
    {
        // Party of 4 fits both a standalone resource and the group; the first candidate must be a
        // standalone resource (deprioritization — combinable resources fill last).
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_PrefersStandaloneResource_WhenPartyFitsBoth));
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 4, DateTime.UtcNow.AddDays(11));

        Assert.NotEmpty(candidates);
        Assert.False(candidates[0].IsGroup);
    }

    [Fact]
    public async Task BuildCandidates_OffersGroupOnly_WhenNoStandaloneResourceFits()
    {
        // Party of 8: no single 4-place resource fits, but the group (CombinedCapacity 8) does.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_OffersGroupOnly_WhenNoStandaloneResourceFits));
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 8, DateTime.UtcNow.AddDays(12));

        ResourceCandidate group = Assert.Single(candidates);
        Assert.True(group.IsGroup);
        Assert.Equal(8, group.Capacity);
        Assert.Equal(1, group.ResourceGroupId);
    }

    [Fact]
    public async Task BuildCandidates_ExcludesGroup_WhenAnyMemberIsBooked()
    {
        // Book one member (T3) of the group; the group can't be assigned for an overlapping slot.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_ExcludesGroup_WhenAnyMemberIsBooked));
        DateTime date = DateTime.UtcNow.AddDays(13);
        db.Bookings.Add(new Booking
        {
            Id = 1, VenueId = 1, ResourceId = 3, SectionId = 1, Date = date,
            BookingRef = "BG1", EndTime = date.AddMinutes(60)
        });
        db.SaveChanges();
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 8, date);

        Assert.DoesNotContain(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
    }

    [Fact]
    public async Task BuildCandidates_ExcludesGroup_WhenAnyMemberIsHeld()
    {
        // Hold one member (T4) of the group; the group can't be assigned.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_ExcludesGroup_WhenAnyMemberIsHeld));
        DateTime date = DateTime.UtcNow.AddDays(14);
        var holdService = new HoldService(new UtcClock());
        Assert.NotNull(holdService.PlaceHold(venueId: 1, resourceId: 4, sectionId: 1, bookingDate: date));
        ResourceAutoAssigner assigner = CreateAssigner(db, holdService);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 8, date);

        Assert.DoesNotContain(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
    }

    [Fact]
    public async Task BuildCandidates_OffersGroupedResourcesIndividually_AfterUngroupedOnesOfTheSameSize()
    {
        // T3/T4 fit 4 each on their own, so a party of 4 must still be able to take one —
        // combining them only deprioritizes them, so they fill after the ungrouped T1/T2 and keep the
        // merged 8-place option open as long as possible.
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_OffersGroupedResourcesIndividually_AfterUngroupedOnesOfTheSameSize));
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 4, DateTime.UtcNow.AddDays(15));

        var singleResourceIds = candidates.Where(c => !c.IsGroup).Select(c => c.ResourceId).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, singleResourceIds);
        Assert.Contains(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
        // …and the group itself still sorts last of all.
        Assert.True(candidates[^1].IsGroup);
    }

    [Fact]
    public async Task BuildCandidates_AppliesOversizeCap_ToGroups()
    {
        // MaxSpareCapacity = 2: a party of 2 at the 8-place group exceeds the cap (8 - 2 = 6 > 2).
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_AppliesOversizeCap_ToGroups));
        venue.MaxSpareCapacity = 2;
        db.SaveChanges();
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 2, DateTime.UtcNow.AddDays(16));

        Assert.DoesNotContain(candidates, c => c.IsGroup && c.ResourceGroupId == 1);
    }

    [Fact]
    public async Task BuildCandidates_ReturnsEmpty_WhenNothingFits()
    {
        // Party of 10 exceeds both the standalone resources (4) and the group (8).
        var (venue, db) = SeedWithGroup(nameof(BuildCandidates_ReturnsEmpty_WhenNothingFits));
        ResourceAutoAssigner assigner = CreateAssigner(db);

        IReadOnlyList<ResourceCandidate> candidates = await assigner.BuildCandidatesAsync(venue, partySize: 10, DateTime.UtcNow.AddDays(17));

        Assert.Empty(candidates);
    }
}
