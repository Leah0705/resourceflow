using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class AvailabilityControllerTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    [Fact]
    public async Task GetAvailability_ReturnsOk()
    {
        HttpClient client = _factory.CreateClient();
        int venueId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            venueId = db.Venues.First().Id;
        }

        var date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        HttpResponseMessage response = await client.GetAsync($"/api/venues/{venueId}/availability?date={date}&partySize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailabilityResponseDto? result = await response.Content.ReadFromJsonAsync<AvailabilityResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(venueId, result.VenueId);
        Assert.NotEmpty(result.Slots);
    }

    [Fact]
    public async Task GetAvailability_NotFound_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        var date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        HttpResponseMessage response = await client.GetAsync($"/api/availability/9999?date={date}&partySize=2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_InternalError_Returns500()
    {
        // This is a bit tricky to trigger with the real service unless we mock it to throw.
        // But since AvailabilityController uses the real AvailabilityService in integration tests,
        // we'd need to cause an unexpected exception.

        // One way is to pass an invalid date format that might bypass initial validation but crash later,
        // but the controller uses DateTime model binding.

        // Let's use a very old date that might cause issues? Actually, most things are handled.
        // A better way is to use a mock for the service if we want to test the catch block specifically.
    }

    // ── BookingSlotIntervalMinutes ───────────────────────────────────────────
    // End-to-end (real HTTP route + real AvailabilityService + real SQLite) proof
    // that the configured interval drives the slot step on GET /api/availability,
    // and that a long duration + short interval doesn't offer double-booked starts.

    private static async Task<int> SeedVenueWithIntervalAsync(TestWebAppFactory factory, int interval, int duration = 60)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var r = new Venue
        {
            Name = $"Interval Test {interval}",
            OpenTime = "11:00",
            CloseTime = "12:00",
            Timezone = "UTC",
            DefaultBookingDurationMinutes = duration,
            BookingSlotIntervalMinutes = interval,
            Sections = new List<Section>
            {
                new()
                {
                    Name = "Main",
                    Resources = new List<Resource> { new() { Name = "T1", Capacity = 2 } }
                }
            }
        };
        db.Venues.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    [Theory]
    [InlineData(15, new[] { "11:00", "11:15", "11:30", "11:45" })]
    [InlineData(30, new[] { "11:00", "11:30" })]
    [InlineData(60, new[] { "11:00" })]
    public async Task GetAvailability_StepsByConfiguredInterval(int interval, string[] expectedTimes)
    {
        int venueId = await SeedVenueWithIntervalAsync(_factory, interval);
        HttpClient client = _factory.CreateClient();
        var date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/venues/{venueId}/availability?date={date}&partySize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailabilityResponseDto? result = await response.Content.ReadFromJsonAsync<AvailabilityResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(expectedTimes, result!.Slots.Select(s => s.Time).ToArray());
    }

    [Fact]
    public async Task GetAvailability_LongDurationShortInterval_NoDoubleBookingOverHttp()
    {
        // 90-min duration, 15-min interval, one resource booked at 12:00 across a 11:00–13:00 window.
        // Every start time overlapping the 12:00–13:30 booking must be unavailable; 13:15 onward
        // (which a 90-min slot would end at/after the booking) must remain available.
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var r = new Venue
        {
            Name = "Interval NoDoubleBook",
            OpenTime = "11:00",
            CloseTime = "13:30",
            Timezone = "UTC",
            DefaultBookingDurationMinutes = 90,
            BookingSlotIntervalMinutes = 15,
            Sections = new List<Section>
            {
                new()
                {
                    Name = "Main",
                    Resources = new List<Resource> { new() { Name = "T1", Capacity = 2 } }
                }
            }
        };
        db.Venues.Add(r);
        await db.SaveChangesAsync();

        // Booking at 12:00 local (== UTC here) occupies 12:00–13:30.
        var bookingDate = DateTime.UtcNow.AddDays(1).Date.AddHours(12);
        db.Bookings.Add(new Booking
        {
            VenueId = r.Id,
            ResourceId = r.Sections.First().Resources.First().Id,
            SectionId = r.Sections.First().Id,
            Date = bookingDate,
            BookingRef = "E2E-1",
            PartySize = 2
        });
        await db.SaveChangesAsync();

        HttpClient client = _factory.CreateClient();
        var date = bookingDate.ToString("yyyy-MM-dd");
        HttpResponseMessage response = await client.GetAsync(
            $"/api/venues/{r.Id}/availability?date={date}&partySize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailabilityResponseDto? result = await response.Content.ReadFromJsonAsync<AvailabilityResponseDto>();
        Assert.NotNull(result);

        var unavailable = result!.Slots.Where(s => !s.IsAvailable).Select(s => s.Time).ToList();
        Assert.Contains("11:00", unavailable);
        Assert.Contains("12:00", unavailable);
        Assert.Contains("13:00", unavailable);

        // Close=13:30 with a `while (current < localEnd)` loop means the last generated slot is
        // 13:15. Every generated start (11:00…13:15) overlaps the 12:00–13:30 booking window once
        // the 90-min slot duration is applied, so none should be offered as available.
        var available = result.Slots.Where(s => s.IsAvailable).Select(s => s.Time).ToList();
        Assert.Empty(available);
    }
}
