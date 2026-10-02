using Microsoft.AspNetCore.Mvc;
using Moq;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Controllers;

/// <summary>
/// Covers the combinable-resource group hold path and the auto-assign path of
/// <see cref="HoldsController.PlaceHold"/>. The single-resource path lives in
/// <see cref="HoldsControllerUnitTests"/>.
/// </summary>
public class HoldsControllerGroupHoldTests
{
    private readonly Mock<IHoldService> _mockHoldService = new();
    private readonly Mock<IHoldPolicyService> _mockPolicy = new();
    private readonly Mock<IBookingRepository> _mockBookingRepository = new();
    private readonly Mock<IResourceGroupRepository> _mockResourceGroupRepository = new();
    private readonly HoldsController _controller;

    public HoldsControllerGroupHoldTests()
    {
        // ResourceAutoAssigner is sealed, so it is composed from mocked dependencies rather than mocked.
        ResourceAutoAssigner autoAssigner = new(_mockBookingRepository.Object, _mockHoldService.Object);
        _controller = new HoldsController(
            _mockHoldService.Object, _mockPolicy.Object, autoAssigner, _mockResourceGroupRepository.Object,
            new HoldClientQuota(_mockHoldService.Object, HoldClientQuota.DefaultMaxActiveHolds));
    }

    private static readonly DateTime BookingDate = DateTime.UtcNow.Date.AddDays(1).AddHours(19);

    private static PlaceHoldRequest GroupRequest(int partySize = 6, int groupId = 10) => new()
    {
        VenueId = 1,
        ResourceGroupId = groupId,
        PartySize = partySize,
        Date = BookingDate
    };

    private static ResourceGroup GroupWithMembers(int combinedCapacity = 8, params (int ResourceId, int SectionId)[] members)
    {
        var group = new ResourceGroup { Id = 10, VenueId = 1, CombinedCapacity = combinedCapacity };
        foreach ((int resourceId, int sectionId) in members)
        {
            group.Members.Add(new ResourceGroupMembership
            {
                ResourceGroupId = group.Id,
                ResourceId = resourceId,
                Resource = new Resource { Id = resourceId, SectionId = sectionId, Capacity = 4 }
            });
        }
        return group;
    }

    private void SetupEligiblePolicy(Venue? venue = null)
        => _mockPolicy.Setup(p => p.ValidateAnyResourceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(HoldPolicyResult.Eligible(
                venue ?? new Venue { Id = 1, DefaultBookingDurationMinutes = 90 }, BookingDate));

    [Fact]
    public async Task PlaceHold_ReturnsBadRequest_WhenBothResourceIdAndResourceGroupIdProvided()
    {
        PlaceHoldRequest request = GroupRequest();
        request.ResourceId = 5;

        var result = await _controller.PlaceHold(request);

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        MessageResponse msg = Assert.IsType<MessageResponse>(bad.Value);
        Assert.Equal("Specify either ResourceId or ResourceGroupId, not both.", msg.Message);
        _mockPolicy.Verify(p => p.ValidateAnyResourceAsync(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsNotFound_WhenVenueMissing()
    {
        _mockPolicy.Setup(p => p.ValidateAnyResourceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(HoldPolicyResult.NotFound());

        var result = await _controller.PlaceHold(GroupRequest());

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Venue not found.", Assert.IsType<MessageResponse>(notFound.Value).Message);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsBadRequest_WhenPolicyRejects()
    {
        _mockPolicy.Setup(p => p.ValidateAnyResourceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(HoldPolicyResult.Rejected("The venue is closed at the requested time."));

        var result = await _controller.PlaceHold(GroupRequest());

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The venue is closed at the requested time.",
            Assert.IsType<MessageResponse>(bad.Value).Message);
        _mockResourceGroupRepository.Verify(
            r => r.GetByIdWithMembersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsConflict_WhenPolicyReportsExistingBooking()
    {
        _mockPolicy.Setup(p => p.ValidateAnyResourceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(HoldPolicyResult.Booked("This resource is already booked for that time."));

        var result = await _controller.PlaceHold(GroupRequest());

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("This resource is already booked for that time.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsNotFound_WhenGroupDoesNotExist()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1)).ReturnsAsync((ResourceGroup?)null);

        var result = await _controller.PlaceHold(GroupRequest());

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Resource group not found.", Assert.IsType<MessageResponse>(notFound.Value).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PlaceGroupHold_ReturnsBadRequest_WhenPartySizeNotPositive(int partySize)
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));

        var result = await _controller.PlaceHold(GroupRequest(partySize));

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Party size is required for a group hold",
            Assert.IsType<MessageResponse>(bad.Value).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsConflict_WhenPartyExceedsCombinedCapacity()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 9));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("This group has a combined capacity of 8, but 9 guests were requested.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
        _mockHoldService.Verify(
            s => s.PlaceGroupHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsConflict_WhenGroupIsTooLargeForTheOversizeCap()
    {
        SetupEligiblePolicy(new Venue { Id = 1, DefaultBookingDurationMinutes = 90, MaxSpareCapacity = 2 });
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 5));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("This group has a combined capacity of 8, which is too large for a party of 5.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    [Fact]
    public async Task PlaceGroupHold_AllowsGroup_WhenOversizeCapIsSatisfied()
    {
        SetupEligiblePolicy(new Venue { Id = 1, DefaultBookingDurationMinutes = 90, MaxSpareCapacity = 3 });
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));
        _mockHoldService.Setup(s => s.PlaceGroupHold(1, 10, It.IsAny<IReadOnlyList<int>>(), 1, BookingDate, null, 90))
            .Returns(new HoldResult("group-hold-1", BookingDate.AddMinutes(5)));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 5));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsConflict_WhenGroupHasNoMembers()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 6));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("This resource group has no members.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsConflict_WhenAMemberIsAlreadyHeld()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));
        _mockHoldService.Setup(s => s.PlaceGroupHold(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns((HoldResult?)null);

        var result = await _controller.PlaceHold(GroupRequest(partySize: 6));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("One of the combined resources is already held by another user. Please try again shortly.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    [Fact]
    public async Task PlaceGroupHold_ReturnsOk_WithGroupIdAndMembersResolvedFromThePersistedGroup()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (9, 3), (4, 2)));

        DateTime expiry = BookingDate.AddMinutes(5);
        IReadOnlyList<int>? capturedMembers = null;
        int capturedSectionId = -1;
        _mockHoldService.Setup(s => s.PlaceGroupHold(
                1, 10, It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), BookingDate, It.IsAny<string?>(), 90))
            .Callback<int, int, IReadOnlyList<int>, int, DateTime, string?, int>(
                (_, _, members, sectionId, _, _, _) => { capturedMembers = members; capturedSectionId = sectionId; })
            .Returns(new HoldResult("group-hold-1", expiry));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 6));

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        HoldResponse response = Assert.IsType<HoldResponse>(ok.Value);
        Assert.Equal("group-hold-1", response.HoldId);
        Assert.Equal(expiry, response.ExpiresAt);
        Assert.Equal(10, response.ResourceGroupId);
        // A group hold reserves the group, not one resource, so the single-resource fields stay null.
        Assert.Null(response.ResourceId);
        Assert.Null(response.SectionId);
        Assert.Equal(new[] { 9, 4 }, capturedMembers);
        // Section comes from the lowest-numbered member resource (4 → section 2), not from request order.
        Assert.Equal(2, capturedSectionId);
    }

    [Fact]
    public async Task PlaceGroupHold_FallsBackToSectionZero_WhenTheLowestMemberHasNoLoadedResource()
    {
        SetupEligiblePolicy();
        var group = new ResourceGroup { Id = 10, VenueId = 1, CombinedCapacity = 8 };
        group.Members.Add(new ResourceGroupMembership { ResourceGroupId = 10, ResourceId = 4, Resource = null });
        group.Members.Add(new ResourceGroupMembership
        {
            ResourceGroupId = 10,
            ResourceId = 9,
            Resource = new Resource { Id = 9, SectionId = 3, Capacity = 4 }
        });
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1)).ReturnsAsync(group);

        int capturedSectionId = -1;
        _mockHoldService.Setup(s => s.PlaceGroupHold(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Callback<int, int, IReadOnlyList<int>, int, DateTime, string?, int>(
                (_, _, _, sectionId, _, _, _) => capturedSectionId = sectionId)
            .Returns(new HoldResult("group-hold-1", BookingDate.AddMinutes(5)));

        var result = await _controller.PlaceHold(GroupRequest(partySize: 6));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, capturedSectionId);
    }

    [Fact]
    public async Task PlaceGroupHold_ForwardsCurrentHoldId_SoTheCallersOwnHoldIsReplacedNotBlocking()
    {
        SetupEligiblePolicy();
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(10, 1))
            .ReturnsAsync(GroupWithMembers(8, (1, 1), (2, 1)));
        _mockHoldService.Setup(s => s.PlaceGroupHold(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new HoldResult("group-hold-2", BookingDate.AddMinutes(5)));

        PlaceHoldRequest request = GroupRequest(partySize: 6);
        request.CurrentHoldId = "previous-hold";

        var result = await _controller.PlaceHold(request);

        Assert.IsType<OkObjectResult>(result);
        _mockHoldService.Verify(s => s.PlaceGroupHold(
            1, 10, It.IsAny<IReadOnlyList<int>>(), 1, BookingDate, "previous-hold", 90), Times.Once);
    }

    // ── Auto-assign path ────────────────────────────────────────────────────

    private static PlaceHoldRequest AutoAssignRequest(int partySize = 2) => new()
    {
        VenueId = 1,
        PartySize = partySize,
        Date = BookingDate
    };

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task PlaceAutoAssignedHold_ReturnsBadRequest_WhenPartySizeNotPositive(int partySize)
    {
        SetupEligiblePolicy();

        var result = await _controller.PlaceHold(AutoAssignRequest(partySize));

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Party size is required for auto-assign",
            Assert.IsType<MessageResponse>(bad.Value).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceAutoAssignedHold_ReturnsConflict_WhenNoResourceFitsTheParty()
    {
        // A venue with no sections yields no candidates at all.
        SetupEligiblePolicy(new Venue { Id = 1, DefaultBookingDurationMinutes = 60 });

        var result = await _controller.PlaceHold(AutoAssignRequest(partySize: 4));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("No resources are available for the requested time and party size.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    private static Venue VenueWithOneResource(int capacity = 4) => new()
    {
        Id = 1,
        DefaultBookingDurationMinutes = 60,
        Sections =
        [
            new Section
            {
                Id = 1,
                Resources = [new Resource { Id = 7, SectionId = 1, Capacity = capacity }]
            }
        ]
    };

    [Fact]
    public async Task PlaceAutoAssignedHold_ReturnsConflict_WhenEveryCandidateIsHeldByAnotherUser()
    {
        SetupEligiblePolicy(VenueWithOneResource());
        _mockHoldService.Setup(s => s.PlaceAutoHold(
                It.IsAny<int>(), It.IsAny<IReadOnlyList<ResourceCandidate>>(),
                It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns((AutoAssignResult?)null);

        var result = await _controller.PlaceHold(AutoAssignRequest(partySize: 4));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("All suitable resources are currently being held by other users. Please try again shortly.",
            Assert.IsType<MessageResponse>(conflict.Value).Message);
    }

    [Fact]
    public async Task PlaceAutoAssignedHold_ReturnsOk_WithTheResolvedResourceAndSection()
    {
        SetupEligiblePolicy(VenueWithOneResource());
        DateTime expiry = BookingDate.AddMinutes(5);
        _mockHoldService.Setup(s => s.PlaceAutoHold(
                1, It.IsAny<IReadOnlyList<ResourceCandidate>>(), BookingDate, It.IsAny<string?>(), 60))
            .Returns(new AutoAssignResult("auto-hold-1", expiry, ResourceId: 7, SectionId: 1));

        var result = await _controller.PlaceHold(AutoAssignRequest(partySize: 4));

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        HoldResponse response = Assert.IsType<HoldResponse>(ok.Value);
        Assert.Equal("auto-hold-1", response.HoldId);
        Assert.Equal(expiry, response.ExpiresAt);
        Assert.Equal(7, response.ResourceId);
        Assert.Equal(1, response.SectionId);
    }
}
