using Microsoft.Extensions.Configuration;
using Moq;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// The admin's confirmation-email preview. What it renders has to be the real template applied to
/// the real brand row — a preview that agreed with nothing is worse than none.
/// </summary>
public class EmailPreviewServiceTests
{
    private sealed class FixedClock(DateTime now) : ISystemClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private static readonly DateTime Now = new(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IVenueRepository> _venues = new();
    private readonly Mock<IBrandSettingsRepository> _brandRepo = new();

    private static Venue Location(int id, string name) => new()
    {
        Id = id,
        Name = name,
        Address = $"{id} Example Street",
        Timezone = "UTC",
        OpenTime = "11:00",
        CloseTime = "23:00",
    };

    private EmailPreviewService Service(params Venue[] locations)
    {
        _venues.Setup(r => r.GetAllActiveWithSectionsAsync()).ReturnsAsync([.. locations]);
        _brandRepo.Setup(b => b.GetAsync()).ReturnsAsync(new BrandSettings
        {
            AppName = "North Workspace",
            PrimaryColor = "#aa3311",
            WebsiteUrl = "https://bookings.example.com",
        });

        var configuration = new ConfigurationBuilder().Build();
        return new EmailPreviewService(
            _venues.Object,
            new BrandService(_brandRepo.Object, configuration),
            new EmailTemplateService(),
            new FixedClock(Now));
    }

    [Fact]
    public async Task Preview_RendersTheNamedLocation()
    {
        EmailPreviewResult result = await Service(Location(1, "Riverside"), Location(2, "Old Town"))
            .BuildConfirmationPreviewAsync(2);

        Assert.Equal(2, result.VenueId);
        Assert.Equal("Old Town", result.VenueName);
        Assert.Contains("Old Town", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_FallsBackToTheFirstLocationWhenNoneIsNamed()
    {
        EmailPreviewResult result = await Service(Location(1, "Riverside"), Location(2, "Old Town"))
            .BuildConfirmationPreviewAsync(null);

        Assert.Equal(1, result.VenueId);
        Assert.Equal("Riverside", result.VenueName);
    }

    /// <summary>An id that names nothing is a stale selection, not an error worth a broken panel.</summary>
    [Fact]
    public async Task Preview_FallsBackToTheFirstLocationWhenTheIdIsUnknown()
    {
        EmailPreviewResult result = await Service(Location(1, "Riverside")).BuildConfirmationPreviewAsync(99);

        Assert.Equal(1, result.VenueId);
    }

    /// <summary>
    /// A fresh install has no locations yet, and the brand settings are exactly what an admin is
    /// looking at the preview to check.
    /// </summary>
    [Fact]
    public async Task Preview_UsesAPlaceholderLocationWhenTheInstanceHasNone()
    {
        EmailPreviewResult result = await Service().BuildConfirmationPreviewAsync(null);

        Assert.Null(result.VenueId);
        Assert.Equal(EmailPreviewSample.PlaceholderVenue.Name, result.VenueName);
        Assert.Contains("North Workspace", result.Html, StringComparison.Ordinal);
    }

    /// <summary>The subject is the send path's own, not a second copy of the wording.</summary>
    [Fact]
    public async Task Preview_CarriesTheSubjectTheSendPathUses()
    {
        Venue location = Location(1, "Riverside");

        EmailPreviewResult result = await Service(location).BuildConfirmationPreviewAsync(null);

        Assert.Equal(new EmailTemplateService().BuildConfirmationSubject(location), result.Subject);
    }

    [Fact]
    public async Task Preview_RendersTheBrandColourAndManageLink()
    {
        EmailPreviewResult result = await Service(Location(1, "Riverside")).BuildConfirmationPreviewAsync(null);

        Assert.Contains("#aa3311", result.Html, StringComparison.Ordinal);
        Assert.Contains(
            $"https://bookings.example.com/booking-confirmation/{EmailPreviewSample.RefFor(BookingRefFormat.AlphaNumeric)}",
            result.Html,
            StringComparison.Ordinal);
    }

    /// <summary>The sample booking is rendered, never written — the preview is a read.</summary>
    [Fact]
    public async Task Preview_PersistsNothing()
    {
        await Service(Location(1, "Riverside")).BuildConfirmationPreviewAsync(null);

        _venues.Verify(r => r.SaveChangesAsync(), Times.Never);
        _brandRepo.Verify(b => b.SaveChangesAsync(), Times.Never);
        _brandRepo.Verify(b => b.AddAsync(It.IsAny<BrandSettings>()), Times.Never);
    }
}
