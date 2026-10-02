using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// What a guest receives, rendered for the admin without sending anything. The point of it is
/// that it goes through the same <see cref="IEmailTemplateService"/> the send path does rather
/// than a second copy of the markup — a preview that mimicked the template would drift from it
/// silently, and the drift would only show up in a guest's inbox.
/// </summary>
public sealed class EmailPreviewService(
    IVenueRepository venues,
    BrandService brandService,
    IEmailTemplateService templateService,
    ISystemClock clock)
{
    private readonly IVenueRepository _venues = venues;
    private readonly BrandService _brandService = brandService;
    private readonly IEmailTemplateService _templateService = templateService;
    private readonly ISystemClock _clock = clock;

    /// <summary>
    /// Renders the confirmation for a stand-in booking at <paramref name="venueId"/>, or at
    /// the first location when none is named. Falls back to a placeholder location so an instance
    /// with nothing set up yet still previews its branding.
    /// </summary>
    public async Task<EmailPreviewResult> BuildConfirmationPreviewAsync(int? venueId)
    {
        List<Venue> active = await _venues.GetAllActiveWithSectionsAsync();
        Venue venue = active.FirstOrDefault(r => r.Id == venueId)
            ?? active.FirstOrDefault()
            ?? EmailPreviewSample.PlaceholderVenue;

        BrandSettings brand = await _brandService.GetAsync();
        string websiteUrl = _brandService.GetWebsiteUrl(brand);

        Booking booking = EmailPreviewSample.BookingFor(venue, _clock.UtcNow);

        return new EmailPreviewResult
        {
            VenueId = venue.Id == 0 ? null : venue.Id,
            VenueName = venue.Name,
            RecipientEmail = EmailPreviewSample.CustomerEmail,
            Subject = _templateService.BuildConfirmationSubject(venue),
            Html = _templateService.BuildConfirmationEmail(booking, venue, brand, websiteUrl),
        };
    }
}

public class EmailPreviewResult
{
    public int? VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
}
