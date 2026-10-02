using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Extensions;
using ResourceFlowApi.Infrastructure.Auth;

namespace ResourceFlowApi.Controllers;

[ApiController]
[EnableRateLimiting("public")]
public class WaitlistController(WaitlistService waitlistService) : ControllerBase
{
    private readonly WaitlistService _waitlist = waitlistService;

    // Joining creates a row, and leaving or setting push acts on one by its reference, so those
    // take the tight booking-lookup ceiling like cancelling a booking does. Status is polled
    // while the guest waits and stays on "public"; the entry reference is 100 bits of CSPRNG
    // output, so it is not worth guessing at either rate.
    // <seealso>WaitlistControllerTests.Join_CarriesTheTightLookupPolicy</seealso>
    // <seealso>WaitlistControllerTests.LeaveAndSetPush_CarryTheTightLookupPolicy</seealso>
    [HttpPost("api/venues/{venueId:int}/waitlist")]
    [EnableRateLimiting(ServiceCollectionExtensions.BookingLookupPolicy)]
    public async Task<IActionResult> Join(int venueId, [FromBody] JoinWaitlistRequest req)
    {
        WaitlistStatusDto status = await _waitlist.JoinAsync(venueId, req);
        return CreatedAtAction(nameof(GetStatus), new { entryRef = status.Ref }, status);
    }

    [HttpGet("api/venues/{venueId:int}/waitlist")]
    public async Task<IActionResult> GetQuote(int venueId, [FromQuery] int partySize = 2)
    {
        return Ok(await _waitlist.GetQuoteAsync(venueId, Math.Clamp(partySize, BookingLimits.MinPartySize, BookingLimits.MaxPartySize)));
    }

    [HttpGet("api/waitlist/{entryRef}")]
    public async Task<IActionResult> GetStatus(string entryRef)
    {
        WaitlistStatusDto? status = await _waitlist.GetStatusAsync(entryRef);
        return status == null ? EntryNotFound() : Ok(status);
    }

    [HttpPost("api/waitlist/{entryRef}/leave")]
    [EnableRateLimiting(ServiceCollectionExtensions.BookingLookupPolicy)]
    public async Task<IActionResult> Leave(string entryRef)
    {
        return await _waitlist.LeaveAsync(entryRef) ? NoContent() : EntryNotFound();
    }

    [HttpPut("api/waitlist/{entryRef}/push")]
    [EnableRateLimiting(ServiceCollectionExtensions.BookingLookupPolicy)]
    public async Task<IActionResult> SetPush(string entryRef, [FromBody] WaitlistPushRequest req)
    {
        return await _waitlist.SetPushAsync(entryRef, req) ? NoContent() : EntryNotFound();
    }

    [HttpGet("api/admin/venues/{venueId:int}/waitlist")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetBoard(int venueId)
    {
        return Ok(await _waitlist.GetBoardAsync(venueId));
    }

    [HttpPost("api/admin/venues/{venueId:int}/waitlist")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> AddByStaff(int venueId, [FromBody] JoinWaitlistRequest req)
    {
        WaitlistEntryDto entry = await _waitlist.AddByStaffAsync(venueId, req);
        return CreatedAtAction(nameof(GetBoard), new { venueId }, entry);
    }

    [HttpPost("api/admin/waitlist/{id:int}/notify")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> Notify(int id)
    {
        await _waitlist.NotifyAsync(id);
        return NoContent();
    }

    [HttpPost("api/admin/waitlist/{id:int}/assign")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignWaitlistEntryRequest req)
    {
        return Ok(await _waitlist.AssignAsync(id, req));
    }

    [HttpPost("api/admin/waitlist/{id:int}/remove")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> Remove(int id)
    {
        await _waitlist.RemoveAsync(id);
        return NoContent();
    }

    private NotFoundObjectResult EntryNotFound() =>
        NotFound(new MessageResponse { Message = "Waitlist entry not found.", Code = ErrorCodes.WaitlistNotFound });
}
