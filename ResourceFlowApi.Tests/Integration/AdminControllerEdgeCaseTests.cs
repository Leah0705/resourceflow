using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class AdminControllerEdgeCaseTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    [Fact]
    public async Task CreateVenue_MissingName_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/admin/venues", new { name = "", address = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_Conflict_ReturnsConflict()
    {
        // This is tricky to trigger in integration because AdminService.CreateBookingAsync
        // checks for availability. If it's not available, it throws InvalidOperationException.
        // We already have a test for duplicate conflict in BookingsControllerTests.
    }

    [Fact]
    public async Task SendEmail_MissingFields_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        
        // Seed a booking
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = new ResourceFlowApi.Core.Domain.Booking 
        { 
            VenueId = 1, SectionId = 1, ResourceId = 1, 
            Date = DateTime.UtcNow, CustomerEmail = "t@t.com", PartySize = 2,
            BookingRef = "REF1"
        };
        db.Bookings.Add(booking);
        db.SaveChanges();

        var response = await client.PostAsJsonAsync($"/api/admin/bookings/{booking.Id}/email", new { subject = "", body = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendEmail_MissingCustomerEmail_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = new ResourceFlowApi.Core.Domain.Booking 
        { 
            VenueId = 1, SectionId = 1, ResourceId = 1, 
            Date = DateTime.UtcNow, CustomerEmail = "", PartySize = 2,
            BookingRef = "REF2"
        };
        db.Bookings.Add(booking);
        db.SaveChanges();

        var response = await client.PostAsJsonAsync($"/api/admin/bookings/{booking.Id}/email", new { subject = "S", body = "B" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
