using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Services;

public class BookingConfirmationServiceTests
{
    private static BookingConfirmationService CreateService(
        AppDbContext db,
        Mock<IEmailService>? emailServiceMock = null,
        EmailSettingsService? emailSettingsService = null,
        IEmailFailureRepository? emailFailureRepository = null)
    {
        emailServiceMock ??= new Mock<IEmailService>();
        var config = new Mock<IConfiguration>();
        var brand = new BrandService(new BrandSettingsRepository(db), config.Object);
        var template = new EmailTemplateService();
        return new BookingConfirmationService(
            emailSettingsService,
            emailServiceMock.Object,
            emailFailureRepository,
            template,
            brand);
    }

    // Builds a persisted-shape Booking + Venue pair without needing the full
    // CreateBookingAsync pipeline (these tests exercise the email layer in isolation).
    private static (Booking booking, Venue venue) MakeBookingAndVenue(
        AppDbContext db,
        string venueName = "Test Venue",
        string? imageUrl = null,
        string customerEmail = "guest@example.com",
        string? customerName = null)
    {
        var venue = new Venue
        {
            Id = 1,
            Name = venueName,
            ImageUrl = imageUrl,
            Timezone = "UTC",
            DefaultBookingDurationMinutes = 120,
        };
        var booking = new Booking
        {
            Id = 1,
            VenueId = 1,
            Venue = venue,
            BookingRef = "ABC123",
            CustomerEmail = customerEmail,
            CustomerName = customerName,
            PartySize = 2,
            Date = new DateTime(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 8, 1, 21, 0, 0, DateTimeKind.Utc),
        };
        db.Venues.Add(venue);
        db.Bookings.Add(booking);
        db.SaveChanges();
        return (booking, venue);
    }

    private static EmailSettingsService MockSettingsService(EmailSettings settings)
    {
        // EmailSettingsService is concrete-injected; the cheapest stub for these tests is a
        // Moq'd instance whose GetAsync() returns the seeded settings. 4 null! ctor args.
        var mock = new Mock<EmailSettingsService>(null!, null!, null!, null!);
        mock.Setup(s => s.GetAsync()).ReturnsAsync(settings);
        return mock.Object;
    }

    // ── Send path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendConfirmationAsync_SendsEmail_WhenEnabled()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_SendsEmail_WhenEnabled));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        var settings = new EmailSettings { SendBookingConfirmations = true };
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(settings));

        await svc.SendConfirmationAsync(booking, venue);

        emailServiceMock.Verify(
            e => e.SendEmailAsync("guest@example.com", It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SendConfirmationAsync_EmailHtml_ContainsVenueImage_WhenImageUrlSet()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_EmailHtml_ContainsVenueImage_WhenImageUrlSet));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db, imageUrl: "https://cdn.example.com/photo.jpg", venueName: "Pic Venue");
        string? capturedBody = null;
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }));

        await svc.SendConfirmationAsync(booking, venue);

        Assert.NotNull(capturedBody);
        Assert.Contains("https://cdn.example.com/photo.jpg", capturedBody);
    }

    [Fact]
    public async Task SendConfirmationAsync_EmailHtml_ContainsFaviconSvg_WhenNoVenueImageButFaviconSet()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_EmailHtml_ContainsFaviconSvg_WhenNoVenueImageButFaviconSet));
        db.Set<BrandSettings>().Add(new BrandSettings { FaviconIcon = "building" });
        (Booking booking, Venue venue) = MakeBookingAndVenue(db, venueName: "Icon Venue");
        string? capturedBody = null;
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }));

        await svc.SendConfirmationAsync(booking, venue);

        Assert.NotNull(capturedBody);
        Assert.Contains("/api/brand/pwa-icon.svg", capturedBody);
        Assert.DoesNotContain("cdn.example.com", capturedBody);
    }

    [Fact]
    public async Task SendConfirmationAsync_EmailHtml_HasNoImage_WhenNeitherVenueImageNorFaviconSet()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_EmailHtml_HasNoImage_WhenNeitherVenueImageNorFaviconSet));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db, venueName: "Plain Venue");
        string? capturedBody = null;
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }));

        await svc.SendConfirmationAsync(booking, venue);

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("<img", capturedBody);
    }

    // ── Skip path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendConfirmationAsync_DoesNotSendEmail_WhenConfirmationsDisabled()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_DoesNotSendEmail_WhenConfirmationsDisabled));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = false }));

        await svc.SendConfirmationAsync(booking, venue);

        emailServiceMock.Verify(
            e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SendConfirmationAsync_DoesNotSendEmail_WhenCustomerEmailMissing()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_DoesNotSendEmail_WhenCustomerEmailMissing));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db, customerEmail: null!);
        var emailServiceMock = new Mock<IEmailService>();
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }));

        await svc.SendConfirmationAsync(booking, venue);

        emailServiceMock.Verify(
            e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SendConfirmationAsync_DoesNotSendEmail_WhenSettingsServiceNull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_DoesNotSendEmail_WhenSettingsServiceNull));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        // No emailSettingsService passed — null short-circuit must skip the send entirely.
        BookingConfirmationService svc = CreateService(db, emailServiceMock, emailSettingsService: null);

        await svc.SendConfirmationAsync(booking, venue);

        emailServiceMock.Verify(
            e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SendConfirmationAsync_DoesNotSendEmail_WhenEmailServiceNull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_DoesNotSendEmail_WhenEmailServiceNull));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        // Pass a null IEmailService directly (Mock.Object is non-null, so use the ctor).
        var config = new Mock<IConfiguration>();
        var svc = new BookingConfirmationService(
            MockSettingsService(new EmailSettings { SendBookingConfirmations = true }),
            emailService: null,
            emailFailureRepository: null,
            new EmailTemplateService(),
            new BrandService(new BrandSettingsRepository(db), config.Object));

        // Must not throw despite null emailService.
        await svc.SendConfirmationAsync(booking, venue);
    }

    // ── Failure path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendConfirmationAsync_NeverThrows_WhenEmailSendFails()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_NeverThrows_WhenEmailSendFails));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP failure"));
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }));

        // Best-effort contract: must not propagate the SMTP exception.
        await svc.SendConfirmationAsync(booking, venue);
    }

    [Fact]
    public async Task SendConfirmationAsync_PersistsEmailFailure_WhenSendThrowsAndRepositoryConfigured()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_PersistsEmailFailure_WhenSendThrowsAndRepositoryConfigured));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP failure"));
        var failureRepo = new EmailFailureRepository(db);
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }), failureRepo);

        await svc.SendConfirmationAsync(booking, venue);

        List<EmailFailure> failures = await db.Set<EmailFailure>().ToListAsync();
        var failure = Assert.Single(failures);
        Assert.Equal("ABC123", failure.BookingRef);
        Assert.Equal("guest@example.com", failure.RecipientEmail);
        Assert.Contains("SMTP failure", failure.ErrorMessage);
    }

    [Fact]
    public async Task SendConfirmationAsync_DoesNotPersistFailure_WhenRepositoryNull()
    {
        using AppDbContext db = TestDbFactory.Create(nameof(SendConfirmationAsync_DoesNotPersistFailure_WhenRepositoryNull));
        (Booking booking, Venue venue) = MakeBookingAndVenue(db);
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP failure"));
        // No IEmailFailureRepository passed — the catch must still not throw, and must not persist.
        BookingConfirmationService svc = CreateService(db, emailServiceMock, MockSettingsService(new EmailSettings { SendBookingConfirmations = true }), emailFailureRepository: null);

        await svc.SendConfirmationAsync(booking, venue);

        Assert.Empty(await db.Set<EmailFailure>().ToListAsync());
    }
}
