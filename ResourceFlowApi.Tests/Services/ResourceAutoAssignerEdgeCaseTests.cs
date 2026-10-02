using Moq;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// Guard-clause and degenerate-shape branches of <see cref="ResourceAutoAssigner.BuildCandidatesAsync"/>.
/// These build the venue graph in memory rather than through EF so the null/empty navigation
/// shapes an EF Include can't produce (a section with no Resources collection, a membership whose Resource
/// was never loaded) are reachable at all.
/// </summary>
public class ResourceAutoAssignerEdgeCaseTests
{
    private readonly Mock<IBookingRepository> _bookingRepository = new();
    private readonly Mock<IHoldService> _holdService = new();

    private ResourceAutoAssigner CreateAssigner() => new(_bookingRepository.Object, _holdService.Object);

    private static readonly DateTime BookingDate = DateTime.UtcNow.AddDays(1);

    [Fact]
    public async Task BuildCandidates_ReturnsEmpty_WhenTheVenueHasNoSectionsCollection()
    {
        var venue = new Venue { Id = 1, Sections = null! };

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, 2, BookingDate));
    }

    [Fact]
    public async Task BuildCandidates_ReturnsEmpty_WhenTheVenueHasNoSections()
    {
        var venue = new Venue { Id = 1, Sections = [] };

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, 2, BookingDate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task BuildCandidates_ReturnsEmpty_WhenPartySizeIsNotPositive(int partySize)
    {
        var venue = new Venue
        {
            Id = 1,
            Sections = [new Section { Id = 1, Resources = [new Resource { Id = 1, SectionId = 1, Capacity = 4 }] }]
        };

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, partySize, BookingDate));
    }

    [Fact]
    public async Task BuildCandidates_SkipsSectionsWithoutAResourcesCollection()
    {
        var venue = new Venue
        {
            Id = 1,
            Sections =
            [
                new Section { Id = 1, Resources = null! },
                new Section { Id = 2, Resources = [new Resource { Id = 5, SectionId = 2, Capacity = 4 }] }
            ]
        };

        IReadOnlyList<ResourceCandidate> candidates =
            await CreateAssigner().BuildCandidatesAsync(venue, 2, BookingDate);

        ResourceCandidate only = Assert.Single(candidates);
        Assert.Equal(5, only.ResourceId);
        Assert.Equal(2, only.SectionId);
    }

    [Fact]
    public async Task BuildCandidates_SkipsGroupsWithNoMembers()
    {
        var venue = new Venue
        {
            Id = 1,
            Sections = [new Section { Id = 1, Resources = [] }],
            Groups = [new ResourceGroup { Id = 1, VenueId = 1, CombinedCapacity = 8 }]
        };

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, 6, BookingDate));
    }

    [Fact]
    public async Task BuildCandidates_SkipsGroupsSmallerThanTheParty()
    {
        var venue = new Venue
        {
            Id = 1,
            Sections = [new Section { Id = 1, Resources = [] }],
            Groups = [GroupOf(combinedCapacity: 4, (1, 1), (2, 1))]
        };

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, 6, BookingDate));
    }

    [Fact]
    public async Task BuildCandidates_AnchorsGroupSectionToZero_WhenTheLowestMemberResourceIsNotLoaded()
    {
        var group = new ResourceGroup { Id = 1, VenueId = 1, CombinedCapacity = 8 };
        group.Members.Add(new ResourceGroupMembership { ResourceGroupId = 1, ResourceId = 2, Resource = null });
        group.Members.Add(new ResourceGroupMembership
        {
            ResourceGroupId = 1,
            ResourceId = 9,
            Resource = new Resource { Id = 9, SectionId = 3, Capacity = 4 }
        });

        var venue = new Venue { Id = 1, Sections = [new Section { Id = 1, Resources = [] }], Groups = [group] };

        ResourceCandidate only = Assert.Single(await CreateAssigner().BuildCandidatesAsync(venue, 6, BookingDate));
        Assert.True(only.IsGroup);
        Assert.Equal(2, only.ResourceId);
        Assert.Equal(0, only.SectionId);
        Assert.Equal(new[] { 2, 9 }, only.Members);
    }

    [Fact]
    public async Task BuildCandidates_ExcludesGroup_WhenTheGroupItselfIsAlreadyBooked()
    {
        var venue = new Venue
        {
            Id = 1,
            Sections = [new Section { Id = 1, Resources = [] }],
            Groups = [GroupOf(combinedCapacity: 8, (1, 1), (2, 1))]
        };
        _bookingRepository
            .Setup(r => r.IsUnitBookedOnDateAsync(null, 1, It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        Assert.Empty(await CreateAssigner().BuildCandidatesAsync(venue, 6, BookingDate));
        // The per-member hold check is short-circuited once the group is known to be booked.
        _holdService.Verify(
            s => s.IsResourceHeld(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildCandidates_AppliesTheOversizeCapToStandaloneResources()
    {
        var venue = new Venue
        {
            Id = 1,
            MaxSpareCapacity = 1,
            Sections =
            [
                new Section
                {
                    Id = 1,
                    Resources =
                    [
                        new Resource { Id = 1, SectionId = 1, Capacity = 3 },
                        new Resource { Id = 2, SectionId = 1, Capacity = 8 }
                    ]
                }
            ]
        };

        ResourceCandidate only = Assert.Single(await CreateAssigner().BuildCandidatesAsync(venue, 2, BookingDate));
        Assert.Equal(1, only.ResourceId);
    }

    private static ResourceGroup GroupOf(int combinedCapacity, params (int ResourceId, int SectionId)[] members)
    {
        var group = new ResourceGroup { Id = 1, VenueId = 1, CombinedCapacity = combinedCapacity };
        foreach ((int resourceId, int sectionId) in members)
        {
            group.Members.Add(new ResourceGroupMembership
            {
                ResourceGroupId = 1,
                ResourceId = resourceId,
                Resource = new Resource { Id = resourceId, SectionId = sectionId, Capacity = 4 }
            });
        }
        return group;
    }
}
