using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("public")]
public class HoldsController(
    IHoldService holdService,
    IHoldPolicyService holdPolicyService,
    ResourceAutoAssigner autoAssigner,
    IResourceGroupRepository resourceGroupRepository,
    HoldClientQuota clientQuota) : ControllerBase
{
    private readonly IHoldService _holdService = holdService;
    private readonly IHoldPolicyService _holdPolicyService = holdPolicyService;
    private readonly ResourceAutoAssigner _autoAssigner = autoAssigner;
    private readonly IResourceGroupRepository _resourceGroupRepository = resourceGroupRepository;
    private readonly HoldClientQuota _clientQuota = clientQuota;

    /// <summary>
    /// Places a temporary hold on a resource for a given date.
    /// Returns 409 Conflict if the resource is already held by someone else.
    /// When <see cref="PlaceHoldRequest.ResourceId"/>/<see cref="PlaceHoldRequest.SectionId"/>
    /// are omitted, the server auto-assigns the best available resource across all sections.
    /// When <see cref="PlaceHoldRequest.ResourceGroupId"/> is set, the server reserves all member
    /// resources of that combinable group as one hold.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PlaceHold([FromBody] PlaceHoldRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // A combinable-group hold takes precedence and resolves its own members.
        if (request.ResourceGroupId.HasValue)
        {
            // A group hold must not also specify a concrete resource — that's ambiguous.
            if (request.ResourceId.HasValue)
            {
                return BadRequest(new MessageResponse
                {
                    Message = "Specify either ResourceId or ResourceGroupId, not both.",
                    Code = ErrorCodes.HoldAmbiguousGroupAndResource
                });
            }

            return await PlaceGroupHold(request);
        }

        bool autoAssign = request.ResourceId is null && request.SectionId is null;
        if (request.ResourceId is null ^ request.SectionId is null)
        {
            return BadRequest(new MessageResponse
            {
                Message = "Specify both ResourceId and SectionId, or omit both for auto-assign.",
                Code = ErrorCodes.BookingAmbiguousResourceSelection
            });
        }

        HoldPolicyResult policy = autoAssign
            ? await _holdPolicyService.ValidateAnyResourceAsync(request.VenueId, request.Date)
            : await _holdPolicyService.ValidateAsync(request.VenueId, request.ResourceId!.Value, request.Date, request.PartySize);

        return policy.Status switch
        {
            HoldPolicyStatus.NotFound => NotFound(new MessageResponse { Message = "Venue not found.", Code = policy.Code }),
            HoldPolicyStatus.Rejected => BadRequest(new MessageResponse { Message = policy.FailureMessage!, Code = policy.Code, Args = policy.Args }),
            HoldPolicyStatus.Booked => Conflict(new MessageResponse { Message = policy.FailureMessage!, Code = policy.Code, Args = policy.Args }),
            _ => autoAssign
                ? await PlaceAutoAssignedHold(request, policy)
                : PlaceEligibleHold(request, policy)
        };
    }

    /// <summary>
    /// Resolves a combinable group's members and places a single multi-resource hold on all of them.
    /// Validates venue-level policy, that the group exists + belongs to the venue,
    /// and that the party fits within the group's CombinedCapacity (and the oversize cap). The actual
    /// all-members-free check + place happens atomically inside HoldService.PlaceGroupHold's lock.
    /// </summary>
    private async Task<IActionResult> PlaceGroupHold(PlaceHoldRequest request)
    {
        HoldPolicyResult policy = await _holdPolicyService.ValidateAnyResourceAsync(request.VenueId, request.Date);
        if (policy.Status != HoldPolicyStatus.Eligible)
        {
            return policy.Status switch
            {
                HoldPolicyStatus.NotFound => NotFound(new MessageResponse { Message = "Venue not found.", Code = policy.Code }),
                HoldPolicyStatus.Rejected => BadRequest(new MessageResponse { Message = policy.FailureMessage!, Code = policy.Code, Args = policy.Args }),
                HoldPolicyStatus.Booked => Conflict(new MessageResponse { Message = policy.FailureMessage!, Code = policy.Code, Args = policy.Args }),
                _ => BadRequest(new MessageResponse { Message = "Hold not available.", Code = ErrorCodes.HoldUnavailable })
            };
        }

        ResourceGroup? group = await _resourceGroupRepository.GetByIdWithMembersAsync(request.ResourceGroupId!.Value, request.VenueId);
        if (group == null)
        {
            return NotFound(new MessageResponse { Message = "Resource group not found.", Code = ErrorCodes.ResourceGroupNotFound });
        }

        if (group.HasWalkInOnlyMember())
        {
            return Conflict(new MessageResponse { Message = "This resource is kept for walk-ins and can't be booked online.", Code = ErrorCodes.ResourceWalkInOnly });
        }

        if (request.PartySize <= 0)
        {
            return BadRequest(new MessageResponse
            {
                Message = "Party size is required for a group hold so the server can validate combined capacity.",
                Code = ErrorCodes.HoldGroupPartySizeRequired
            });
        }

        if (request.PartySize > group.CombinedCapacity)
        {
            return Conflict(new MessageResponse
            {
                Message = $"This group has a combined capacity of {group.CombinedCapacity}, but {request.PartySize} guests were requested.",
                Code = ErrorCodes.ResourceGroupCapacityExceeded
            });
        }

        Venue venue = policy.Venue!;
        if (venue.ExceedsOversizeCap(group.CombinedCapacity, request.PartySize))
        {
            return Conflict(new MessageResponse
            {
                Message = $"This group has a combined capacity of {group.CombinedCapacity}, which is too large for a party of {request.PartySize}.",
                Code = ErrorCodes.ResourceGroupOversizeCap
            });
        }

        var memberIds = group.Members.Select(m => m.ResourceId).ToList();
        if (memberIds.Count == 0)
        {
            return Conflict(new MessageResponse { Message = "This resource group has no members.", Code = ErrorCodes.ResourceGroupNoMembers });
        }

        int sectionId = group.Members.OrderBy(m => m.ResourceId).First().Resource?.SectionId ?? 0;

        HoldResult? result = _holdService.PlaceGroupHold(
            request.VenueId,
            group.Id,
            memberIds,
            sectionId,
            policy.BookingDate,
            request.CurrentHoldId,
            BookingDuration.For(venue, request.PartySize));

        if (result == null)
        {
            return Conflict(new MessageResponse
            {
                Message = "One of the combined resources is already held by another user. Please try again shortly.",
                Code = ErrorCodes.ResourceGroupHoldConflict
            });
        }

        if (RejectIfOverClientQuota(result.HoldId) is { } overQuota)
        {
            return overQuota;
        }

        return Ok(new HoldResponse
        {
            HoldId = result.HoldId,
            ExpiresAt = result.ExpiresAt,
            ResourceGroupId = group.Id
        });
    }

    private IActionResult PlaceEligibleHold(PlaceHoldRequest request, HoldPolicyResult policy)
    {
        HoldResult? result = _holdService.PlaceHold(
            request.VenueId,
            request.ResourceId!.Value,
            request.SectionId!.Value,
            policy.BookingDate,
            request.CurrentHoldId,
            BookingDuration.For(policy.Venue!, request.PartySize));

        if (result == null)
        {
            return Conflict(new MessageResponse { Message = "This resource is already held by another user. Please select a different resource or try again shortly.", Code = ErrorCodes.BookingResourceHeld });
        }

        if (RejectIfOverClientQuota(result.HoldId) is { } overQuota)
        {
            return overQuota;
        }

        return Ok(new HoldResponse
        {
            HoldId = result.HoldId,
            ExpiresAt = result.ExpiresAt
        });
    }

    private async Task<IActionResult> PlaceAutoAssignedHold(PlaceHoldRequest request, HoldPolicyResult policy)
    {
        if (request.PartySize <= 0)
        {
            return BadRequest(new MessageResponse
            {
                Message = "Party size is required for auto-assign so the server can pick a resource that fits your party.",
                Code = ErrorCodes.HoldAutoAssignPartySizeRequired
            });
        }

        IReadOnlyList<ResourceCandidate> candidates = await _autoAssigner.BuildCandidatesAsync(
            policy.Venue!, request.PartySize, policy.BookingDate);

        if (candidates.Count == 0)
        {
            return Conflict(new MessageResponse
            {
                Message = "No resources are available for the requested time and party size.",
                Code = ErrorCodes.BookingNoResourcesAvailable
            });
        }

        AutoAssignResult? result = _holdService.PlaceAutoHold(
            request.VenueId,
            candidates,
            policy.BookingDate,
            request.CurrentHoldId,
            BookingDuration.For(policy.Venue!, request.PartySize));

        if (result == null)
        {
            return Conflict(new MessageResponse
            {
                Message = "All suitable resources are currently being held by other users. Please try again shortly.",
                Code = ErrorCodes.BookingAllResourcesHeld
            });
        }

        if (RejectIfOverClientQuota(result.HoldId) is { } overQuota)
        {
            return overQuota;
        }

        return Ok(new HoldResponse
        {
            HoldId = result.HoldId,
            ExpiresAt = result.ExpiresAt,
            ResourceId = result.ResourceId,
            SectionId = result.SectionId
        });
    }

    /// <summary>
    /// Counts a newly placed hold against the caller's quota. When the caller already has the
    /// maximum number of live holds, the new hold is released again and a 429 is returned.
    /// </summary>
    private ObjectResult? RejectIfOverClientQuota(string holdId)
    {
        string clientKey = HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_clientQuota.TryAdmit(clientKey, holdId))
        {
            return null;
        }

        _holdService.ReleaseHold(holdId);
        return StatusCode(StatusCodes.Status429TooManyRequests, new MessageResponse
        {
            Message = $"You already hold {_clientQuota.MaxActiveHolds} resources. Book or release one before holding another.",
            Code = ErrorCodes.HoldClientLimit,
            Args = new Dictionary<string, object> { ["max"] = _clientQuota.MaxActiveHolds }
        });
    }

    /// <summary>
    /// Releases a hold early (e.g., when the user navigates away).
    /// Safe to call even if the hold has already expired.
    /// </summary>
    [HttpDelete("{holdId}")]
    public IActionResult ReleaseHold(string holdId)
    {
        _holdService.ReleaseHold(holdId);
        return NoContent();
    }
}
