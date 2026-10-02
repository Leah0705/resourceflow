using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;

namespace ResourceFlowApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("public")]
public class AvailabilityController(IAvailabilityService availabilityService) : ControllerBase
{
    private readonly IAvailabilityService _availabilityService = availabilityService;

    [HttpGet("/api/venues/{venueId}/availability")]
    public async Task<IActionResult> Get(int venueId, [FromQuery] DateTime date, [FromQuery] int partySize)
    {
        // NotFoundException (venue not found) → 404 and any unexpected exception
        // → 500 are mapped by GlobalExceptionHandler with a { message } body.
        AvailabilityResponseDto result = await _availabilityService.GetAvailabilityAsync(venueId, date, partySize);
        return Ok(result);
    }
}
