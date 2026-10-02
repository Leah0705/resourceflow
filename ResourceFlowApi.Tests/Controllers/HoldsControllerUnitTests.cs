using Microsoft.AspNetCore.Mvc;
using Moq;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Controllers;

public class HoldsControllerUnitTests
{
    private readonly Mock<IHoldService> _mockHoldService;
    private readonly Mock<IHoldPolicyService> _mockPolicy;
    private readonly HoldsController _controller;

    public HoldsControllerUnitTests()
    {
        _mockHoldService = new Mock<IHoldService>();
        _mockPolicy = new Mock<IHoldPolicyService>();
        // ResourceAutoAssigner is sealed, so we instantiate it directly. The explicit-resource path
        // under test never invokes it; the auto-assign tests below substitute via the policy mock.
        ResourceAutoAssigner autoAssigner = new(new Mock<IBookingRepository>().Object, _mockHoldService.Object);
        _mockResourceGroupRepository = new Mock<IResourceGroupRepository>();
        _controller = new HoldsController(
            _mockHoldService.Object, _mockPolicy.Object, autoAssigner, _mockResourceGroupRepository.Object,
            new HoldClientQuota(_mockHoldService.Object, HoldClientQuota.DefaultMaxActiveHolds));
    }

    private readonly Mock<IResourceGroupRepository> _mockResourceGroupRepository;

    private static PlaceHoldRequest ExplicitRequest(DateTime date) => new()
    {
        VenueId = 1,
        ResourceId = 1,
        SectionId = 1,
        Date = date
    };

    [Fact]
    public async Task PlaceHold_ReturnsBadRequest_WhenModelStateInvalid()
    {
        _controller.ModelState.AddModelError("Error", "Message");
        var result = await _controller.PlaceHold(ExplicitRequest(DateTime.UtcNow.AddDays(1)));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PlaceHold_ReturnsBadRequest_WhenOnlyOneOfResourceOrSectionProvided()
    {
        var result = await _controller.PlaceHold(new PlaceHoldRequest
        {
            VenueId = 1,
            ResourceId = 1,
            // SectionId omitted
            Date = DateTime.UtcNow.AddDays(1)
        });

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        MessageResponse msg = Assert.IsType<MessageResponse>(bad.Value);
        Assert.Contains("Specify both ResourceId and SectionId", msg.Message);
    }

    [Fact]
    public async Task PlaceHold_ReturnsNotFound_WhenPolicyReturnsNotFound()
    {
        _mockPolicy.Setup(p => p.ValidateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(HoldPolicyResult.NotFound());

        var result = await _controller.PlaceHold(ExplicitRequest(DateTime.UtcNow.AddDays(1)));

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        MessageResponse msg = Assert.IsType<MessageResponse>(notFound.Value);
        Assert.Equal("Venue not found.", msg.Message);
    }

    [Fact]
    public async Task PlaceHold_ReturnsBadRequest_WhenPolicyRejects()
    {
        _mockPolicy.Setup(p => p.ValidateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(HoldPolicyResult.Rejected("no."));

        var result = await _controller.PlaceHold(ExplicitRequest(DateTime.UtcNow.AddDays(1)));

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        MessageResponse msg = Assert.IsType<MessageResponse>(bad.Value);
        Assert.Equal("no.", msg.Message);
    }

    [Fact]
    public async Task PlaceHold_ReturnsConflict_WhenPolicyReportsExistingBooking()
    {
        _mockPolicy.Setup(p => p.ValidateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(HoldPolicyResult.Booked("taken."));

        var result = await _controller.PlaceHold(ExplicitRequest(DateTime.UtcNow.AddDays(1)));

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        MessageResponse msg = Assert.IsType<MessageResponse>(conflict.Value);
        Assert.Equal("taken.", msg.Message);
        // HoldService must not be touched when policy already rejected.
        _mockHoldService.Verify(
            s => s.PlaceHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceHold_BlocksTheResourceForThePartysDurationRule()
    {
        var venue = new Venue
        {
            Id = 1, DefaultBookingDurationMinutes = 60,
            DurationRulesJson = """[{"minPartySize":1,"minutes":60},{"minPartySize":5,"minutes":120}]""",
        };
        var date = DateTime.UtcNow.AddDays(1);
        _mockPolicy.Setup(p => p.ValidateAsync(1, 1, date, 5))
            .ReturnsAsync(HoldPolicyResult.Eligible(venue, date));
        _mockHoldService.Setup(s => s.PlaceHold(1, 1, 1, date, null, 120))
            .Returns(new HoldResult("h1", date));

        PlaceHoldRequest request = ExplicitRequest(date);
        request.PartySize = 5;
        var result = await _controller.PlaceHold(request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task PlaceHold_ReturnsConflict_ForAGroupHoldingAWalkInOnlyResource()
    {
        var venue = new Venue { Id = 1, DefaultBookingDurationMinutes = 60 };
        var date = DateTime.UtcNow.AddDays(1);
        _mockPolicy.Setup(p => p.ValidateAnyResourceAsync(1, date))
            .ReturnsAsync(HoldPolicyResult.Eligible(venue, date));
        _mockResourceGroupRepository.Setup(r => r.GetByIdWithMembersAsync(7, 1)).ReturnsAsync(new ResourceGroup
        {
            Id = 7, VenueId = 1, CombinedCapacity = 6,
            Members =
            [
                new() { ResourceId = 1, Resource = new Resource { Id = 1, Capacity = 2 } },
                new() { ResourceId = 2, Resource = new Resource { Id = 2, Capacity = 4, WalkInOnly = true } },
            ],
        });

        var result = await _controller.PlaceHold(new PlaceHoldRequest { VenueId = 1, ResourceGroupId = 7, PartySize = 5, Date = date });

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(ErrorCodes.ResourceWalkInOnly, Assert.IsType<MessageResponse>(conflict.Value).Code);
        _mockHoldService.Verify(
            s => s.PlaceGroupHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceHold_ReturnsConflict_WhenEligibleButAlreadyHeld()
    {
        var venue = new Venue { Id = 1, DefaultBookingDurationMinutes = 60 };
        var date = DateTime.UtcNow.AddDays(1);
        _mockPolicy.Setup(p => p.ValidateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(HoldPolicyResult.Eligible(venue, date));
        _mockHoldService.Setup(s => s.PlaceHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns((HoldResult?)null);

        var result = await _controller.PlaceHold(ExplicitRequest(date));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task PlaceHold_ReturnsOk_AndPassesPolicyBookingDate_WhenEligibleAndAcquired()
    {
        var venue = new Venue { Id = 1, DefaultBookingDurationMinutes = 75 };
        // Policy-normalized date differs from the raw request date — controller must use the normalized one.
        var normalizedDate = DateTime.UtcNow.Date.AddDays(2).AddHours(12);
        var rawDate = DateTime.SpecifyKind(normalizedDate, DateTimeKind.Unspecified);

        _mockPolicy.Setup(p => p.ValidateAsync(It.IsAny<int>(), It.IsAny<int>(), rawDate, It.IsAny<int>()))
            .ReturnsAsync(HoldPolicyResult.Eligible(venue, normalizedDate));
        _mockHoldService.Setup(s => s.PlaceHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), normalizedDate, It.IsAny<string?>(), 75))
            .Returns(new HoldResult("hold-1", DateTime.UtcNow.AddMinutes(5)));

        var result = await _controller.PlaceHold(ExplicitRequest(rawDate));

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        HoldResponse response = Assert.IsType<HoldResponse>(ok.Value);
        Assert.Equal("hold-1", response.HoldId);
        _mockHoldService.Verify(
            s => s.PlaceHold(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), normalizedDate, It.IsAny<string?>(), 75),
            Times.Once);
    }

    [Fact]
    public void ReleaseHold_DelegatesToService_AndReturnsNoContent()
    {
        var result = _controller.ReleaseHold("hold-1");

        Assert.IsType<NoContentResult>(result);
        _mockHoldService.Verify(s => s.ReleaseHold("hold-1"), Times.Once);
    }
}
