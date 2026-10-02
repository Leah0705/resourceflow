using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class WaitlistControllerTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    private int SeedVenue(bool walkInOnly)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var venue = new Venue { Name = "Waitlist Venue", WalkInOnly = walkInOnly };
        venue.Sections.Add(new Section { Name = "Main", Resources = new List<Resource> { new() { Name = "T1", Capacity = 4 } } });
        db.Venues.Add(venue);
        db.SaveChanges();
        return venue.Id;
    }

    [Fact]
    public void Join_CarriesTheTightLookupPolicy()
    {
        MethodInfo join = typeof(WaitlistController).GetMethod(nameof(WaitlistController.Join))!;

        Assert.Equal("booking-lookup", join.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("public", typeof(WaitlistController).GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Theory]
    [InlineData(nameof(WaitlistController.Leave))]
    [InlineData(nameof(WaitlistController.SetPush))]
    public void LeaveAndSetPush_CarryTheTightLookupPolicy(string action)
    {
        MethodInfo method = typeof(WaitlistController).GetMethod(action)!;

        Assert.Equal("booking-lookup", method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public async Task Join_Returns409_AtALocationThatTakesBookings()
    {
        int id = SeedVenue(walkInOnly: false);

        HttpResponseMessage response = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/venues/{id}/waitlist", new { name = "Ada", partySize = 2 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        MessageResponse? body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal(ErrorCodes.WaitlistNotWalkInNow, body!.Code);
    }

    [Fact]
    public async Task GetQuote_SaysWhetherGuestsCanJoin()
    {
        int id = SeedVenue(walkInOnly: false);

        WaitlistQuoteDto? quote = await _factory.CreateClient().GetFromJsonAsync<WaitlistQuoteDto>($"/api/venues/{id}/waitlist?partySize=2");

        Assert.False(quote!.AcceptingGuests);
        Assert.Equal(0, quote.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task GetStatus_Returns404_ForAnUnknownRef()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/api/waitlist/unknown-ref");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Leave_Returns404_ForAnUnknownRef()
    {
        HttpResponseMessage response = await _factory.CreateClient().PostAsync("/api/waitlist/unknown-ref/leave", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Board_Returns401_WithoutAuth()
    {
        int id = SeedVenue(walkInOnly: true);

        HttpResponseMessage response = await _factory.CreateClient().GetAsync($"/api/admin/venues/{id}/waitlist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaffFlow_AddsCallsAndAssignsAParty()
    {
        int id = SeedVenue(walkInOnly: false);
        HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage added = await client.PostAsJsonAsync($"/api/admin/venues/{id}/waitlist", new { name = "Ada", partySize = 3 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        WaitlistEntryDto entry = (await added.Content.ReadFromJsonAsync<WaitlistEntryDto>())!;
        Assert.True(entry.CanAssignNow);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/admin/waitlist/{entry.Id}/notify", null)).StatusCode);

        WaitlistBoardDto board = (await client.GetFromJsonAsync<WaitlistBoardDto>($"/api/admin/venues/{id}/waitlist"))!;
        Assert.Equal("notified", Assert.Single(board.Entries).Status);

        HttpResponseMessage inUse = await client.PostAsJsonAsync($"/api/admin/waitlist/{entry.Id}/assign", new { });
        Assert.Equal(HttpStatusCode.OK, inUse.StatusCode);
        AssignWaitlistEntryResponse result = (await inUse.Content.ReadFromJsonAsync<AssignWaitlistEntryResponse>())!;
        Assert.Equal("inUse", result.Entry.Status);
        Assert.False(string.IsNullOrEmpty(result.BookingRef));

        board = (await client.GetFromJsonAsync<WaitlistBoardDto>($"/api/admin/venues/{id}/waitlist"))!;
        Assert.Empty(board.Entries);
    }

    [Fact]
    public async Task Remove_TakesThePartyOffTheBoard()
    {
        int id = SeedVenue(walkInOnly: false);
        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage added = await client.PostAsJsonAsync($"/api/admin/venues/{id}/waitlist", new { name = "Bo", partySize = 2 });
        WaitlistEntryDto entry = (await added.Content.ReadFromJsonAsync<WaitlistEntryDto>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/admin/waitlist/{entry.Id}/remove", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/waitlist/{entry.Id}/remove", null)).StatusCode);
    }
}
