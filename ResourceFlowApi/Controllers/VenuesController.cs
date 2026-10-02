using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Infrastructure.Auth;

namespace ResourceFlowApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("public")]
public class VenuesController(VenueManagementService service) : ControllerBase
{
    private readonly VenueManagementService _service = service;

    [HttpGet]
    public async Task<IActionResult> Get()
        => Ok(await _service.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        VenueDto? result = await _service.GetByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> Post(VenueDto dto)
    {
        VenueDto created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Write)]
    public async Task<IActionResult> Put(int id, UpdateVenueRequest req)
    {
        // ValidationException (bad DefaultBookingDurationMinutes) → 400 is mapped
        // by GlobalExceptionHandler.
        VenueDto? result = await _service.UpdateAsync(id, req);
        return result == null ? NotFound() : Ok(result);
    }

    // ── Sections ────────────────────────────────────────────────────────────

    [HttpPost("{id}/sections")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> AddSection(int id, CreateSectionRequest req)
    {
        SectionDto? result = await _service.AddSectionAsync(id, req.Name);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}/sections/{sectionId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> UpdateSection(int id, int sectionId, UpdateSectionRequest req)
    {
        SectionDto? result = await _service.UpdateSectionAsync(id, sectionId, req.Name);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}/sections/{sectionId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> DeleteSection(int id, int sectionId)
        => await _service.DeleteSectionAsync(id, sectionId) ? NoContent() : NotFound();

    // Best-effort "what would this delete orphan?" preview for the two-step delete UI.
    // Counts non-cancelled future bookings that would lose their section reference. Falls back to
    // 404 when the section doesn't exist / doesn't belong to the venue, so the UI can degrade
    // to generic copy rather than blocking the delete.
    [HttpGet("{id}/sections/{sectionId}/impact")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetSectionDeleteImpact(int id, int sectionId)
    {
        DeleteImpactDto? result = await _service.GetSectionDeleteImpactAsync(id, sectionId);
        return result == null ? NotFound() : Ok(result);
    }

    // ── Resources ──────────────────────────────────────────────────────────────

    [HttpPost("{id}/sections/{sectionId}/resources")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> AddResource(int id, int sectionId, CreateResourceRequest req)
    {
        ResourceDto? result = await _service.AddResourceAsync(id, sectionId, req.Name, req.Capacity, req.WalkInOnly);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}/sections/{sectionId}/resources/{resourceId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> UpdateResource(int id, int sectionId, int resourceId, UpdateResourceRequest req)
    {
        ResourceDto? result = await _service.UpdateResourceAsync(id, sectionId, resourceId, req.Name, req.Capacity, req.WalkInOnly);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}/sections/{sectionId}/resources/{resourceId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> DeleteResource(int id, int sectionId, int resourceId)
        => await _service.DeleteResourceAsync(id, sectionId, resourceId) ? NoContent() : NotFound();

    // Best-effort "what would this delete orphan?" preview for the two-step delete UI.
    // Counts non-cancelled future bookings that would lose their resource reference. 404 when the resource
    // doesn't exist / doesn't belong to the venue+section, so the UI can fall back to generic copy.
    [HttpGet("{id}/sections/{sectionId}/resources/{resourceId}/impact")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetResourceDeleteImpact(int id, int sectionId, int resourceId)
    {
        DeleteImpactDto? result = await _service.GetResourceDeleteImpactAsync(id, sectionId, resourceId);
        return result == null ? NotFound() : Ok(result);
    }

    // Bookings taken under an older schedule. Editing hours/open days/walk-in policy is
    // silent by design — it leaves existing rows alone — so the admin UI reads this after an edit
    // to show who is now booked into a service the location no longer runs. 404 when the
    // venue doesn't exist, so the caller can drop the panel rather than block the form.
    // Scoped under locations — this is a venue-schedule read, not a resource/section shape read.
    [HttpGet("{id}/schedule-conflicts")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Locations, ApiKeyScopes.Read)]
    public async Task<IActionResult> GetScheduleConflicts(int id)
    {
        List<ScheduleConflictDto>? result = await _service.GetScheduleConflictsAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    // ── Combinable resource groups ─────────────────────────────────────────────
    //
    // CRUD for resource groups. ValidationException (member rules, CombinedCapacity floor) is
    // mapped to 400 by GlobalExceptionHandler; null result → 404.

    [HttpPost("{id}/groups")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> AddResourceGroup(int id, CreateResourceGroupRequest req)
    {
        ResourceGroupDto? result = await _service.AddResourceGroupAsync(id, req);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}/groups/{groupId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> UpdateResourceGroup(int id, int groupId, UpdateResourceGroupRequest req)
    {
        ResourceGroupDto? result = await _service.UpdateResourceGroupAsync(id, groupId, req);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}/groups/{groupId}")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [RequiresScope(ApiKeyScopes.Resources, ApiKeyScopes.Write)]
    public async Task<IActionResult> DeleteResourceGroup(int id, int groupId)
        => await _service.DeleteResourceGroupAsync(id, groupId) ? NoContent() : NotFound();
}
