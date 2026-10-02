using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Infrastructure.Auth;

namespace ResourceFlowApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class AdminController(AdminService adminService) : ControllerBase
{
    public enum bookingStatus { active, cancelled, all, past, upcoming, noshow }
    private readonly AdminService _adminService = adminService;

    // Aggregates counts/lists across venues and bookings; gated on the dominant resource
    // (bookings — TodayBookingsList and the booking counts/occupancy chart are the bulk of the
    // payload) rather than split across two scopes for one read. A key without bookings:read
    // cannot see the dashboard at all; TodayBookingsList is still guest-redacted independently
    // by BookingGuestVisibility when the key also lacks guests:read.
    [HttpGet("overview")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Read)]
    public async Task<IActionResult> Overview()
        => Ok(await _adminService.GetOverviewAsync());

    [HttpGet("bookings")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetBookings(
        [FromQuery] int? venueId,
        [FromQuery] DateTime? date,
        [FromQuery] bookingStatus status = bookingStatus.active,
        [FromQuery] bool cancelled = false,
        [FromQuery] string? email = null,
        [FromQuery] string? bookingRef = null,
        [FromQuery] string? query = null)
    {
        //this is business logic that should likely belong in the service but its OK for now
        string effectiveStatus = cancelled ? nameof(bookingStatus.cancelled) : status.ToString();
        return Ok(await _adminService.GetBookingsAsync(venueId, date, effectiveStatus, email, bookingRef, query));
    }

    [HttpGet("bookings/{id}")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetBooking(int id)
    {
        BookingDetailDto? result = await _adminService.GetBookingAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("bookings")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> CreateBooking([FromBody] AdminCreateBookingRequest req)
    {
        // ValidationException (bad resource/section) → 400, ConflictException (overlap/capacity) → 409
        // are mapped by GlobalExceptionHandler; the controller just orchestrates.
        BookingDetailDto result = await _adminService.CreateBookingAsync(req);
        return CreatedAtAction(nameof(GetBooking), new { id = result.Id }, result);
    }

    [HttpPost("bookings/{id}/extend")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> ExtendBooking(int id, [FromBody] ExtendBookingRequest req)
    {
        DateTime? endTime = await _adminService.ExtendBookingAsync(id, req.Minutes);
        return endTime == null ? NotFound() : Ok(new { endTime });
    }

    [HttpPost("bookings/{id}/status")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> SetBookingStatus(int id, [FromBody] SetBookingStatusRequest req)
    {
        BookingDetailDto? result = await _adminService.SetBookingStatusAsync(id, req.Status);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("bookings/{id}/cancel")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> CancelBooking(int id)
    {
        // ConflictException (past booking) → 409 is mapped by GlobalExceptionHandler.
        return await _adminService.CancelBookingAsync(id) ? NoContent() : NotFound();
    }

    [HttpDelete("bookings/{id}")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> PurgeBooking(int id)
        => await _adminService.PurgeBookingAsync(id) ? NoContent() : NotFound();

    [HttpPost("venues")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> CreateVenue([FromBody] CreateVenueRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            throw new ValidationException("Name is required.") { Code = ErrorCodes.VenueNameRequired };
        }

        VenueDto result = await _adminService.CreateVenueAsync(req.Name, req.Address);
        return CreatedAtAction(nameof(Overview), new { }, result);
    }

    [HttpPatch("venues/{id}")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> PatchVenue(int id, [FromBody] AdminVenuePatchRequest req)
    {
        if (req.IsArchived.HasValue)
        {
            bool success = await _adminService.SetArchivedAsync(id, req.IsArchived.Value);
            if (!success) return NotFound();
        }
        return NoContent();
    }

    [HttpGet("venues/{id}/delete-preview")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetVenueDeletePreview(int id)
    {
        VenueDeletePreviewDto? preview = await _adminService.GetVenueDeletePreviewAsync(id);
        return preview == null ? NotFound() : Ok(preview);
    }

    // Owner-only: the one irreversible cascade in the admin, gated like user management.
    [HttpDelete("venues/{id}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> DeleteVenue(int id)
        => await _adminService.DeleteVenueAsync(id) ? NoContent() : NotFound();

    [HttpPost("venues/{id}/pause")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> PauseBookings(int id, [FromBody] PauseVenueRequest req)
    {
        bool success = await _adminService.PauseVenueBookingsAsync(id, req.Minutes);
        return success ? Ok(new MessageResponse { Message = "Bookings paused successfully." }) : NotFound();
    }

    [HttpPost("venues/{id}/unpause")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> UnpauseBookings(int id)
    {
        bool success = await _adminService.UnpauseVenueBookingsAsync(id);
        return success ? Ok(new MessageResponse { Message = "Bookings unpaused successfully." }) : NotFound();
    }

    [HttpPost("venues/{id}/extend")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> ExtendBookings(int id, [FromBody] ExtendVenueRequest req)
    {
        List<BookingDetailDto>? extendedBookings = await _adminService.ExtendAllActiveBookingsAsync(id, req.Minutes);
        return extendedBookings != null
            ? Ok(new { Message = "Bookings extended successfully.", ExtendedBookings = extendedBookings })
            : NotFound();
    }

    [HttpGet("venues")]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetVenues()
    {
        List<LookupDto> venues = await _adminService.GetVenuesAsync();
        return Ok(venues);
    }

    [HttpGet("venues/{venueId}/sections")]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetSections(int venueId)
    {
        List<LookupDto> sections = await _adminService.GetSectionsAsync(venueId);
        return Ok(sections);
    }

    [HttpPatch("venues/{id}/sections/reorder")]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> ReorderSections(int id, [FromBody] ReorderSectionsRequest req)
    {
        bool? result = await _adminService.ReorderSectionsAsync(id, req.SectionIds);
        return result switch
        {
            null => NotFound(),
            false => BadRequest(new MessageResponse { Message = "sectionIds must include exactly the venue's current sections, with no duplicates.", Code = ErrorCodes.VenueSectionIdsMismatch }),
            true => NoContent(),
        };
    }

    [HttpGet("venues/{venueId}/resources")]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetResources(int venueId)
    {
        List<SectionDto>? result = await _adminService.GetResourcesAsync(venueId);
        if (result == null)
        {
            throw new NotFoundException("Venue not found or has no sections.") { Code = ErrorCodes.VenueNoSections };
        }
        return Ok(result);
    }

    /// <summary>
    /// Sends an admin-authored email to the guest on a booking. Failures are 400s split by cause:
    /// a server with no SMTP settings answers <c>email.not_configured</c>, a send that reached the
    /// transport and failed answers <c>booking.email_send_failed</c>. A caller cannot retry its way
    /// out of the first.
    /// </summary>
    /// <seealso>AdminControllerEmailTests.SendEmail_WhenEmailIsNotConfigured_ReturnsTheNotConfiguredCode</seealso>
    /// <seealso>AdminControllerEmailTests.SendEmail_WhenTheTransportFails_ReturnsTheSendFailedCode</seealso>
    [HttpPost("bookings/{id}/email")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> SendEmail(int id, [FromBody] SendBookingEmailRequest req)
    {
        // Intentionally keeps its catch: SMTP/transport failures (SmtpException,
        // InfrastructureException, etc.) surface here from the email stack and are
        // wrapped as a user-facing 400 "Failed to send: ..." rather than a 500 —
        // this is a deliberate UX choice, not something GlobalExceptionHandler should
        // own (those failures are not domain exceptions).
        try
        {
            SendBookingEmailResult result = await _adminService.SendBookingEmailAsync(id, req);
            return result.Status switch
            {
                SendBookingEmailStatus.NotFound => NotFound(),
                SendBookingEmailStatus.MissingFields => BadRequest(new MessageResponse { Message = "Subject and body are required.", Code = ErrorCodes.BookingEmailFieldsRequired }),
                SendBookingEmailStatus.NoCustomerEmail => BadRequest(new MessageResponse { Message = "Customer email is not available.", Code = ErrorCodes.BookingNoCustomerEmail }),
                _ => Ok(new MessageResponse
                {
                    Message = result.Recipient == null ? "Email sent." : $"Email sent to {result.Recipient}."
                })
            };
        }
        catch (InfrastructureException ex) when (ex.Code == ErrorCodes.EmailNotConfigured)
        {
            // A server with no SMTP settings at all is a permanent setup problem, not the
            // transient send failure the catch below describes. Flattening both into
            // booking.email_send_failed left an API-key caller unable to tell "fix your
            // configuration" from "retry later".
            return BadRequest(new MessageResponse
            {
                Message = ex.Message,
                Code = ErrorCodes.EmailNotConfigured,
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new MessageResponse
            {
                Message = $"Failed to send: {ex.Message}",
                Code = ErrorCodes.BookingEmailSendFailed,
                Args = new Dictionary<string, object> { ["detail"] = ex.Message }
            });
        }
    }

    [HttpPost("bookings/{id}/restore")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> RestoreBooking(int id)
    {
        // BusinessRuleException (booking already active) → 400 is mapped by GlobalExceptionHandler.
        BookingDetailDto? result = await _adminService.RestoreBookingAsync(id);
        return result == null ? NotFound() : Ok(new MessageResponse { Message = "Booking restored successfully." });
    }

    [HttpPut("bookings/{id}")]
    [RequiresScope(ApiKeyScopes.Bookings, ApiKeyScopes.Write)]
    public async Task<IActionResult> AdminUpdateBooking(int id, [FromBody] AdminUpdateBookingRequest req)
    {
        // ValidationException (bad venue/resource) and BusinessRuleException
        // (update-conflict / capacity) → 400 are mapped by GlobalExceptionHandler.
        BookingDetailDto? result = await _adminService.AdminUpdateBookingAsync(id, req);
        return result == null ? NotFound() : Ok(result);
    }
}
