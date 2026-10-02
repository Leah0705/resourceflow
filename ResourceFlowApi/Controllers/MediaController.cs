using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Infrastructure.Auth;

namespace ResourceFlowApi.Controllers;

// Every action here is an upload or a delete — no reads — so the whole controller is brand:write
// (media is grouped with brand/highlights/social-links in the issue's resource resource).
[ApiController]
[Route("api/media")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
[RequiresScope(ApiKeyScopes.Brand, ApiKeyScopes.Write)]
[EnableRateLimiting("public")]
public class MediaController(MediaService mediaService) : ControllerBase
{
    private readonly MediaService _mediaService = mediaService;

    private static readonly string[] _allowedTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long _maxHeroBytes = 5 * 1024 * 1024;
    private const long _maxLocationBytes = 2 * 1024 * 1024;
    private const long _maxGuideBytes = 10 * 1024 * 1024;
    private const string _guideContentType = "application/pdf";

    [HttpPost("hero")]
    [RequestSizeLimit(5 * 1024 * 1024 + 8192)]
    public async Task<IActionResult> UploadHero(IFormFile file)
    {
        if (!_allowedTypes.Contains(file.ContentType))
            return BadRequest(new MessageResponse { Message = "Only JPEG, PNG, and WebP images are accepted.", Code = ErrorCodes.MediaUnsupportedImageType });

        if (file.Length > _maxHeroBytes)
            return BadRequest(new MessageResponse { Message = "Hero image must be under 5 MB.", Code = ErrorCodes.MediaHeroTooLarge });

        await using Stream stream = file.OpenReadStream();
        string url = await _mediaService.UploadHeroAsync(stream, file.ContentType);
        return Ok(new { url });
    }

    [HttpDelete("hero")]
    public async Task<IActionResult> DeleteHero()
    {
        await _mediaService.DeleteHeroAsync();
        return NoContent();
    }

    [HttpPost("location/{id:int}")]
    [RequestSizeLimit(2 * 1024 * 1024 + 8192)]
    public async Task<IActionResult> UploadLocation(int id, IFormFile file)
    {
        if (!_allowedTypes.Contains(file.ContentType))
            return BadRequest(new MessageResponse { Message = "Only JPEG, PNG, and WebP images are accepted.", Code = ErrorCodes.MediaUnsupportedImageType });

        if (file.Length > _maxLocationBytes)
            return BadRequest(new MessageResponse { Message = "Location image must be under 2 MB.", Code = ErrorCodes.MediaLocationImageTooLarge });

        await using Stream stream = file.OpenReadStream();
        string? url = await _mediaService.UploadLocationAsync(id, stream, file.ContentType);
        if (url == null) return NotFound();
        return Ok(new { url });
    }

    [HttpDelete("location/{id:int}")]
    public async Task<IActionResult> DeleteLocation(int id)
    {
        bool found = await _mediaService.DeleteLocationAsync(id);
        if (!found) return NotFound();
        return NoContent();
    }

    [HttpPost("guide/{id:int}")]
    [RequestSizeLimit(10 * 1024 * 1024 + 8192)]
    public async Task<IActionResult> UploadGuide(int id, IFormFile file)
    {
        if (file.ContentType != _guideContentType)
            return BadRequest(new MessageResponse { Message = "Only PDF guide files are accepted.", Code = ErrorCodes.MediaUnsupportedGuideType });

        if (file.Length > _maxGuideBytes)
            return BadRequest(new MessageResponse { Message = "Guide file must be under 10 MB.", Code = ErrorCodes.MediaGuideTooLarge });

        await using Stream stream = file.OpenReadStream();
        string? url = await _mediaService.UploadGuideAsync(id, stream);
        if (url == null) return NotFound();
        return Ok(new { url });
    }

    [HttpDelete("guide/{id:int}")]
    public async Task<IActionResult> DeleteGuide(int id)
    {
        bool found = await _mediaService.DeleteGuideAsync(id);
        if (!found) return NotFound();
        return NoContent();
    }
}
