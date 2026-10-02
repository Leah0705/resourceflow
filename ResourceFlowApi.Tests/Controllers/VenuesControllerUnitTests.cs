using Microsoft.AspNetCore.Mvc;
using Moq;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Controllers;

/// <summary>
/// HTTP-mapping tests for the combinable resource-group CRUD endpoints and the two
/// delete-impact previews, which the integration suite doesn't reach. The controller is a
/// thin mapper, so these pin the null → 404 / value → 200 contract each endpoint promises the admin
/// UI. <see cref="VenueManagementService"/> is concrete with non-virtual methods, so it is
/// composed over mocked repository interfaces — the concrete repositories are internal and gated by
/// <c>[OnlyAccessibleBy]</c>, and controller mapping doesn't need real persistence anyway.
/// </summary>
public class VenuesControllerUnitTests
{
    private readonly Mock<IVenueRepository> _venues = new();
    private readonly Mock<ISectionRepository> _sections = new();
    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IResourceGroupRepository> _groups = new();
    private readonly VenuesController _controller;

    public VenuesControllerUnitTests()
    {
        _groups.Setup(g => g.AddAsync(It.IsAny<ResourceGroup>())).Returns(Task.CompletedTask);
        _groups.Setup(g => g.SaveChangesAsync()).Returns(Task.CompletedTask);

        _controller = new VenuesController(new VenueManagementService(
            _venues.Object, _sections.Object, _resources.Object, _bookings.Object, _groups.Object));
    }

    private static Resource ResourceOf(int id, int capacity = 4, int sectionId = 1) =>
        new() { Id = id, Name = $"T{id}", Capacity = capacity, SectionId = sectionId };

    /// <summary>Venue 1 exists and owns two 4-place resources (ids 1 and 2), with no groups yet.</summary>
    private void SeedVenueWithTwoResources()
    {
        _venues.Setup(r => r.ExistsAsync(1)).ReturnsAsync(true);
        _resources.Setup(t => t.GetManyForVenueAsync(It.IsAny<IReadOnlyList<int>>(), 1))
            .ReturnsAsync((IReadOnlyList<int> ids, int _) => ids.Select(id => ResourceOf(id)).ToList());
        _groups.Setup(g => g.GetAllWithMembersByVenueAsync(1)).ReturnsAsync([]);
    }

    private static ResourceGroup GroupOf(int id = 3, int combinedCapacity = 7, params int[] memberIds)
    {
        var group = new ResourceGroup { Id = id, Name = "Window desks", VenueId = 1, CombinedCapacity = combinedCapacity };
        foreach (int memberId in memberIds)
        {
            group.Members.Add(new ResourceGroupMembership
            {
                ResourceGroupId = id,
                ResourceId = memberId,
                Resource = ResourceOf(memberId)
            });
        }
        return group;
    }

    private static CreateResourceGroupRequest CreateRequest(int combinedCapacity = 7) =>
        new() { Name = "Window desks", Members = [1, 2], CombinedCapacity = combinedCapacity };

    // ── AddResourceGroup ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddResourceGroup_ReturnsOk_WithTheCreatedGroup()
    {
        SeedVenueWithTwoResources();

        var result = await _controller.AddResourceGroup(1, CreateRequest());

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        ResourceGroupDto dto = Assert.IsType<ResourceGroupDto>(ok.Value);
        Assert.Equal("Window desks", dto.Name);
        Assert.Equal(7, dto.CombinedCapacity);
        Assert.Equal([1, 2], dto.Members.Select(m => m.Id));
        _groups.Verify(g => g.AddAsync(It.Is<ResourceGroup>(x => x.CombinedCapacity == 7)), Times.Once);
        _groups.Verify(g => g.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AddResourceGroup_ReturnsNotFound_WhenVenueDoesNotExist()
    {
        _venues.Setup(r => r.ExistsAsync(999)).ReturnsAsync(false);

        var result = await _controller.AddResourceGroup(999, CreateRequest());

        Assert.IsType<NotFoundResult>(result);
        _groups.Verify(g => g.AddAsync(It.IsAny<ResourceGroup>()), Times.Never);
    }

    [Fact]
    public async Task AddResourceGroup_PropagatesValidationException_ForAnInvalidMemberSet()
    {
        // GlobalExceptionHandler maps ValidationException to 400; the controller must not swallow it.
        SeedVenueWithTwoResources();

        await Assert.ThrowsAsync<ValidationException>(() => _controller.AddResourceGroup(
            1, new CreateResourceGroupRequest { Members = [1], CombinedCapacity = 7 }));
    }

    [Fact]
    public async Task AddResourceGroup_PropagatesValidationException_WhenCombinedCapacityExceedsTheMemberSum()
    {
        SeedVenueWithTwoResources();

        await Assert.ThrowsAsync<ValidationException>(() => _controller.AddResourceGroup(1, CreateRequest(combinedCapacity: 9)));
    }

    // ── UpdateResourceGroup ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateResourceGroup_ReturnsOk_WithTheUpdatedGroup()
    {
        SeedVenueWithTwoResources();
        ResourceGroup existing = GroupOf(memberIds: [1, 2]);
        _groups.Setup(g => g.GetByIdWithMembersAsync(3, 1)).ReturnsAsync(existing);

        var result = await _controller.UpdateResourceGroup(1, 3, new UpdateResourceGroupRequest
        {
            Name = "Renamed",
            Members = [1, 2],
            CombinedCapacity = 8
        });

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        ResourceGroupDto dto = Assert.IsType<ResourceGroupDto>(ok.Value);
        Assert.Equal("Renamed", dto.Name);
        Assert.Equal(8, dto.CombinedCapacity);
        _groups.Verify(g => g.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateResourceGroup_ReturnsNotFound_WhenGroupDoesNotExist()
    {
        _groups.Setup(g => g.GetByIdWithMembersAsync(4242, 1)).ReturnsAsync((ResourceGroup?)null);

        var result = await _controller.UpdateResourceGroup(1, 4242, new UpdateResourceGroupRequest
        {
            Members = [1, 2],
            CombinedCapacity = 7
        });

        Assert.IsType<NotFoundResult>(result);
        _groups.Verify(g => g.SaveChangesAsync(), Times.Never);
    }

    // ── DeleteResourceGroup ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteResourceGroup_ReturnsNoContent_AndClearsTheGroupReferenceOnItsBookings()
    {
        ResourceGroup group = GroupOf(memberIds: [1, 2]);
        _groups.Setup(g => g.GetByIdWithMembersAsync(3, 1)).ReturnsAsync(group);
        var booking = new Booking { Id = 1, VenueId = 1, ResourceGroupId = 3, BookingRef = "G1" };
        _bookings.Setup(b => b.GetByResourceGroupAsync(3)).ReturnsAsync([booking]);

        var result = await _controller.DeleteResourceGroup(1, 3);

        Assert.IsType<NoContentResult>(result);
        // The booking survives the delete; only its group reference is cleared.
        Assert.Null(booking.ResourceGroupId);
        _groups.Verify(g => g.Remove(group), Times.Once);
    }

    [Fact]
    public async Task DeleteResourceGroup_ReturnsNotFound_WhenGroupDoesNotExist()
    {
        _groups.Setup(g => g.GetByIdWithMembersAsync(4242, 1)).ReturnsAsync((ResourceGroup?)null);

        var result = await _controller.DeleteResourceGroup(1, 4242);

        Assert.IsType<NotFoundResult>(result);
        _groups.Verify(g => g.Remove(It.IsAny<ResourceGroup>()), Times.Never);
    }

    // ── Delete-impact previews ──────────────────────────────────────────────

    [Fact]
    public async Task GetSectionDeleteImpact_ReturnsOk_WithTheAffectedBookingCount()
    {
        _sections.Setup(s => s.GetWithResourcesForVenueAsync(1, 1))
            .ReturnsAsync(new Section { Id = 1, Name = "Main", VenueId = 1, Resources = [ResourceOf(1), ResourceOf(2)] });
        _bookings.Setup(b => b.CountFutureBySectionOrResourcesAsync(1, It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>()))
            .ReturnsAsync(3);
        _groups.Setup(g => g.GetAllWithMembersByVenueAsync(1)).ReturnsAsync([]);

        var result = await _controller.GetSectionDeleteImpact(1, 1);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, Assert.IsType<DeleteImpactDto>(ok.Value).Bookings);
    }

    [Fact]
    public async Task GetSectionDeleteImpact_ReturnsNotFound_WhenSectionDoesNotBelongToTheVenue()
    {
        _sections.Setup(s => s.GetWithResourcesForVenueAsync(1, 999)).ReturnsAsync((Section?)null);

        var result = await _controller.GetSectionDeleteImpact(999, 1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetResourceDeleteImpact_ReturnsOk_WithTheAffectedBookingCount()
    {
        _resources.Setup(t => t.GetForVenueAsync(1, 1, 1)).ReturnsAsync(ResourceOf(1));
        _bookings.Setup(b => b.CountFutureByResourceAsync(1, It.IsAny<DateTime>())).ReturnsAsync(2);
        _groups.Setup(g => g.GetAllWithMembersByVenueAsync(1)).ReturnsAsync([]);

        var result = await _controller.GetResourceDeleteImpact(1, 1, 1);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, Assert.IsType<DeleteImpactDto>(ok.Value).Bookings);
    }

    [Fact]
    public async Task GetResourceDeleteImpact_AlsoCountsBookingsHeldThroughACombinableGroup()
    {
        // A group booking stores ResourceId = null, so a resource-only count reports zero and the confirm
        // step would claim the delete orphans nothing while a merged-resource party is booked.
        _resources.Setup(t => t.GetForVenueAsync(1, 1, 1)).ReturnsAsync(ResourceOf(1));
        _bookings.Setup(b => b.CountFutureByResourceAsync(1, It.IsAny<DateTime>())).ReturnsAsync(0);
        _groups.Setup(g => g.GetAllWithMembersByVenueAsync(1)).ReturnsAsync([GroupOf(memberIds: [1, 2])]);
        _bookings.Setup(b => b.CountFutureByResourceGroupsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.Contains(3)), It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        var result = await _controller.GetResourceDeleteImpact(1, 1, 1);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, Assert.IsType<DeleteImpactDto>(ok.Value).Bookings);
    }

    [Fact]
    public async Task GetResourceDeleteImpact_SkipsTheGroupQuery_WhenNoGroupCoversTheResource()
    {
        _resources.Setup(t => t.GetForVenueAsync(1, 1, 1)).ReturnsAsync(ResourceOf(1));
        _bookings.Setup(b => b.CountFutureByResourceAsync(1, It.IsAny<DateTime>())).ReturnsAsync(4);
        _groups.Setup(g => g.GetAllWithMembersByVenueAsync(1)).ReturnsAsync([GroupOf(memberIds: [7, 8])]);

        var result = await _controller.GetResourceDeleteImpact(1, 1, 1);

        Assert.Equal(4, Assert.IsType<DeleteImpactDto>(Assert.IsType<OkObjectResult>(result).Value).Bookings);
        _bookings.Verify(
            b => b.CountFutureByResourceGroupsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>()),
            Times.Never);
    }

    [Fact]
    public async Task GetResourceDeleteImpact_ReturnsNotFound_WhenResourceDoesNotExist()
    {
        _resources.Setup(t => t.GetForVenueAsync(4242, 1, 1)).ReturnsAsync((Resource?)null);

        var result = await _controller.GetResourceDeleteImpact(1, 1, 4242);

        Assert.IsType<NotFoundResult>(result);
    }
}
