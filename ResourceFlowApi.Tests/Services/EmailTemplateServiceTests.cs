using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

public class EmailTemplateServiceTests
{
    private readonly EmailTemplateService _svc = new();

    private static readonly BrandSettings _defaultBrand = new()
    {
        AppName = "ResourceFlow",
        PrimaryColor = "#0a7ea4",
    };

    private const string DefaultWebsiteUrl = "https://resourceflow.example";

    private static Venue MakeVenue(string? imageUrl = null, string? address = null) => new()
    {
        Id = 1,
        Name = "Test Venue",
        ImageUrl = imageUrl,
        Address = address,
        Timezone = "UTC",
        DefaultBookingDurationMinutes = 120,
    };

    private static Booking MakeBooking(
        string bookingRef = "REF-XYZ",
        string customerEmail = "guest@example.com",
        string? customerName = null,
        string? specialRequests = null,
        string? sectionName = null,
        string? resourceName = null,
        int? resourceId = 1) => new()
    {
        BookingRef = bookingRef,
        CustomerEmail = customerEmail,
        CustomerName = customerName,
        SpecialRequests = specialRequests,
        PartySize = 2,
        Date = new DateTime(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc),
        EndTime = new DateTime(2026, 8, 1, 21, 0, 0, DateTimeKind.Utc),
        Section = sectionName == null ? null : new Section { Name = sectionName },
        Resource = resourceName == null ? null : new Resource { Name = resourceName },
        ResourceId = resourceId,
    };

    // ── Header variants ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildConfirmationEmail_RendersBannerHeader_WhenVenueHasImageUrl()
    {
        var venue = MakeVenue(imageUrl: "https://cdn.example.com/photo.jpg");

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("https://cdn.example.com/photo.jpg", html);
    }

    [Fact]
    public void BuildConfirmationEmail_PrefixesRelativeImageUrl_WithWebsiteUrl()
    {
        var venue = MakeVenue(imageUrl: "media/venue.jpg");

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("https://resourceflow.example/media/venue.jpg", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersIconHeader_WhenNoVenueImage_ButFaviconSet()
    {
        var venue = MakeVenue(imageUrl: null);
        var brand = new BrandSettings { AppName = "Branded", PrimaryColor = "#ff0000", FaviconIcon = "building" };

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, brand, DefaultWebsiteUrl);

        Assert.Contains("/api/brand/pwa-icon.svg", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersPlainTextHeader_WhenNeitherImageNorFavicon()
    {
        var venue = MakeVenue(imageUrl: null);

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.DoesNotContain("<img", html);
        Assert.Contains(venue.Name, html);
    }

    // ── Lookup URL ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildConfirmationEmail_EncodesLookupUrl_WithBookingRefAndEmail()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        // The lookup URL is /booking-confirmation/{ref}?email={email}; both segments are
        // Uri.EscapeDataString-escaped. Hyphens survive (unreserved); '@' becomes %40.
        Assert.Contains("https://resourceflow.example/booking-confirmation/REF-XYZ?email=guest%40example.com", html);
    }

    [Fact]
    public void BuildConfirmationEmail_TrimsTrailingSlash_FromWebsiteUrl()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, "https://resourceflow.example/");

        Assert.Contains("https://resourceflow.example/booking-confirmation/", html);
        Assert.DoesNotContain("resourceflow.example//booking", html);
    }

    // ── Optional blocks ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildConfirmationEmail_RendersSectionRow_WhenSectionPresent()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(sectionName: "Annex"), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("Section", html);
        Assert.Contains("Annex", html);
    }

    [Fact]
    public void BuildConfirmationEmail_OmitsSectionRow_WhenSectionAbsent()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(sectionName: null), venue, _defaultBrand, DefaultWebsiteUrl);

        // No section label rendered when the booking has no section attached.
        Assert.DoesNotContain(">Section<", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersResourceRow_WhenResourceNameSet()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(resourceName: "Annex-3"), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains(">Resource<", html);
        Assert.Contains("Annex-3", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersResourceIdFallbackLabel_WhenResourceNavMissing_ButIdPresent()
    {
        var venue = MakeVenue();
        // No Resource nav object, but a ResourceId is present — fallback label is "Resource #{id}".
        var booking = MakeBooking(resourceName: null, resourceId: 7);

        string html = _svc.BuildConfirmationEmail(booking, venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("Resource #7", html);
    }

    [Fact]
    public void BuildConfirmationEmail_OmitsResourceRow_WhenNeitherResourceNameNorId()
    {
        var venue = MakeVenue();
        var booking = MakeBooking(resourceName: null, resourceId: null);

        string html = _svc.BuildConfirmationEmail(booking, venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.DoesNotContain(">Resource<", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersSpecialRequests_WhenPresent()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(specialRequests: "Projector needed, please"), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("Special requests", html);
        Assert.Contains("Projector needed, please", html);
    }

    [Fact]
    public void BuildConfirmationHtml_EncodesSpecialRequests_ToPreventHtmlInjection()
    {
        var venue = MakeVenue();
        // A raw <script> in special requests must be HTML-escaped, not rendered as a tag.
        var booking = MakeBooking(specialRequests: "<script>alert(1)</script>");

        string html = _svc.BuildConfirmationEmail(booking, venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void BuildConfirmationEmail_OmitsSpecialRequests_WhenAbsent()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(specialRequests: null), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.DoesNotContain("Special requests", html);
    }

    [Fact]
    public void BuildConfirmationEmail_RendersDirections_WhenAddressSet()
    {
        var venue = MakeVenue(address: "123 Main St");

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("123 Main St", html);
        Assert.Contains("maps/search", html);
    }

    [Fact]
    public void BuildConfirmationEmail_OmitsDirections_WhenAddressAbsent()
    {
        var venue = MakeVenue(address: null);

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.DoesNotContain("maps/search", html);
        Assert.DoesNotContain("Get directions", html);
    }

    // ── Greeting ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildConfirmationEmail_UsesPersonalisedGreeting_WhenCustomerNameSet()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(customerName: "Alice"), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("You're all set, Alice!", html);
    }

    [Fact]
    public void BuildConfirmationEmail_UsesGenericGreeting_WhenCustomerNameAbsent()
    {
        var venue = MakeVenue();

        string html = _svc.BuildConfirmationEmail(MakeBooking(customerName: null), venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains("You're all set!", html);
    }

    // ── Guest pluralisation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "participant")]
    [InlineData(2, "participants")]
    [InlineData(5, "participants")]
    public void BuildConfirmationEmail_PluralisesGuestLabel_Correctly(int partySize, string expectedWord)
    {
        var venue = MakeVenue();
        var booking = MakeBooking();
        booking.PartySize = partySize;

        string html = _svc.BuildConfirmationEmail(booking, venue, _defaultBrand, DefaultWebsiteUrl);

        Assert.Contains($"{partySize} {expectedWord}", html);
    }

    // ── Brand fallbacks ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildConfirmationEmail_FallsBackToDefaultColor_WhenBrandColorNull()
    {
        var venue = MakeVenue();
        var brand = new BrandSettings { AppName = "X", PrimaryColor = null! };

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, brand, DefaultWebsiteUrl);

        Assert.Contains("#0a7ea4", html);
    }

    [Fact]
    public void BuildConfirmationEmail_FallsBackToDefaultAppName_WhenBrandAppNameNull()
    {
        var venue = MakeVenue();
        var brand = new BrandSettings { AppName = null!, PrimaryColor = "#000000" };

        string html = _svc.BuildConfirmationEmail(MakeBooking(), venue, brand, DefaultWebsiteUrl);

        Assert.Contains("ResourceFlow", html);
    }
}
