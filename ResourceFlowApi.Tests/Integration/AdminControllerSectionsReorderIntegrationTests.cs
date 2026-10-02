using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

/// <summary>
/// End-to-end (real ASP.NET Core pipeline + SQLite) coverage for the
/// PATCH /api/admin/venues/{id}/sections/reorder, layered on top of the
/// already-thorough service/controller/migration unit tests. These tests go through
/// the real HTTP pipeline (auth middleware, model binding, routing) and a real
/// SQLite database, exercising the full round trip across the three call sites the
/// investigation identified (admin sections lookup, admin resources lookup, and the
/// public/customer-facing venue read) rather than a single mocked service call.
/// </summary>
public class AdminControllerSectionsReorderIntegrationTests(TestWebAppFactory factory)
    : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    private (int venueId, int firstSectionId, int secondSectionId) GetSeededCentralWorkspaceSectionIds()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.Single(r => r.Name == "Central Workspace");
        List<int> sectionIds = db.Sections
            .Where(s => s.VenueId == venue.Id)
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToList();
        return (venue.Id, sectionIds[0], sectionIds[1]);
    }

    private static async Task<int> CreateVenueAsync(HttpClient client, string name)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/admin/venues", new { name, address = "1 Test St" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<int> AddSectionAsync(HttpClient client, int venueId, string name)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/venues/{venueId}/sections", new { name });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    // ── Auth / validation — real HTTP pipeline ──────────────────────────────────

    [Fact]
    public async Task ReorderSections_WithoutAuth_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int first, int second) = GetSeededCentralWorkspaceSectionIds();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { second, first } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReorderSections_NonExistentVenue_Returns404_ThroughRealPipeline()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/admin/venues/999999/sections/reorder",
            new { sectionIds = new[] { 1, 2 } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReorderSections_MismatchedSectionIds_Returns400_ThroughRealPipeline()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        (int venueId, int first, _) = GetSeededCentralWorkspaceSectionIds();

        // Only one of the venue's two sections supplied — must be rejected.
        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { first } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Full round trip: PATCH reorder → GET reflects new order ─────────────────

    [Fact]
    public async Task ReorderSections_FullRoundTrip_AdminSectionsListReflectsNewOrder()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "Round Trip Venue");
        int a = await AddSectionAsync(client, venueId, "A");
        int b = await AddSectionAsync(client, venueId, "B");
        int c = await AddSectionAsync(client, venueId, "C");

        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { c, a, b } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage getResp = await client.GetAsync($"/api/admin/venues/{venueId}/sections");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        JsonElement sections = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = sections.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(["C", "A", "B"], names);
    }

    [Fact]
    public async Task ReorderSections_FullRoundTrip_PublicCustomerFacingEndpointReflectsNewOrder()
    {
        // This is the cross-boundary scenario the investigation flagged as the real risk:
        // the admin-authenticated reorder call must be visible on the *public,
        // unauthenticated* GET /api/venues/{id} endpoint that both the admin
        // location editor and the customer booking flow consume.
        HttpClient adminClient = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(adminClient, "Cross Boundary Venue");
        int groundFloor = await AddSectionAsync(adminClient, venueId, "Ground floor");
        int annex = await AddSectionAsync(adminClient, venueId, "Annex");
        int rooftop = await AddSectionAsync(adminClient, venueId, "Rooftop Room");

        HttpResponseMessage patchResp = await adminClient.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { rooftop, annex, groundFloor } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpClient publicClient = _factory.CreateClient();
        HttpResponseMessage publicResp = await publicClient.GetAsync($"/api/venues/{venueId}");

        Assert.Equal(HttpStatusCode.OK, publicResp.StatusCode);
        JsonElement body = await publicResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = body.GetProperty("sections").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(["Rooftop Room", "Annex", "Ground floor"], names);
    }

    [Fact]
    public async Task ReorderSections_FullRoundTrip_AdminResourcesEndpointReflectsNewSectionOrder()
    {
        // Third call site from the investigation: AdminService.GetResourcesAsync.
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "Resources Endpoint Venue");
        int first = await AddSectionAsync(client, venueId, "Zebra Room");
        int second = await AddSectionAsync(client, venueId, "Alpha Room");

        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { second, first } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage getResp = await client.GetAsync($"/api/admin/venues/{venueId}/resources");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        JsonElement sections = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = sections.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(["Alpha Room", "Zebra Room"], names);
    }

    // ── Regression: non-reordered behaviour is unaffected ───────────────────────

    [Fact]
    public async Task AddSection_AfterPriorReorder_StillAppendsAtCorrectEndPosition()
    {
        // Regression guard: a manual reorder must not corrupt the "append at end"
        // behaviour that ordinary (non-reordering) section creation relies on.
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "Append After Reorder Venue");
        int a = await AddSectionAsync(client, venueId, "A");
        int b = await AddSectionAsync(client, venueId, "B");

        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { b, a } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage addResp = await client.PostAsJsonAsync(
            $"/api/venues/{venueId}/sections", new { name = "C" });
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
        JsonElement added = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, added.GetProperty("sortOrder").GetInt32());

        HttpResponseMessage getResp = await client.GetAsync($"/api/admin/venues/{venueId}/sections");
        JsonElement sections = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = sections.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(["B", "A", "C"], names);
    }

    [Fact]
    public async Task AdminBookingCreation_StillSucceeds_AgainstResourceInReorderedVenue()
    {
        // Regression guard: existing booking/resource flows that read sections must
        // keep working after a reorder — reordering must not touch resource/booking FKs.
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "Booking Flow Venue");
        int sectionA = await AddSectionAsync(client, venueId, "A");
        int sectionB = await AddSectionAsync(client, venueId, "B");

        HttpResponseMessage resourceResp = await client.PostAsJsonAsync(
            $"/api/venues/{venueId}/sections/{sectionB}/resources", new { name = "T1", capacity = 4 });
        Assert.Equal(HttpStatusCode.OK, resourceResp.StatusCode);
        int resourceId = (await resourceResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { sectionB, sectionA } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage bookingResp = await client.PostAsJsonAsync("/api/admin/bookings", new
        {
            venueId,
            sectionId = sectionB,
            resourceId,
            date = DateTime.UtcNow.AddDays(220).ToString("yyyy-MM-ddT12:00:00"),
            customerEmail = "regression@test.com",
            partySize = 2
        });

        Assert.Equal(HttpStatusCode.Created, bookingResp.StatusCode);
        JsonElement created = await bookingResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("id").GetInt32() > 0);
    }

    // ── Boundary / stress ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderSections_TwelveSections_ReverseOrder_RoundTripsCorrectly()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "Twelve Section Hall");

        var ids = new List<int>();
        for (int i = 1; i <= 12; i++)
        {
            ids.Add(await AddSectionAsync(client, venueId, $"Section{i}"));
        }

        List<int> reversed = ids.AsEnumerable().Reverse().ToList();
        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = reversed });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage getResp = await client.GetAsync($"/api/admin/venues/{venueId}/sections");
        JsonElement sections = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> expectedNames = Enumerable.Range(1, 12).Reverse().Select(i => $"Section{i}").ToList();
        List<string> actualNames = sections.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(expectedNames, actualNames);
    }

    [Fact]
    public async Task ReorderSections_NoOpReorder_SameCurrentOrderResent_LeavesOrderUnchanged()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        int venueId = await CreateVenueAsync(client, "No Op Venue");
        int a = await AddSectionAsync(client, venueId, "A");
        int b = await AddSectionAsync(client, venueId, "B");
        int c = await AddSectionAsync(client, venueId, "C");

        // Resend the exact current order (a, b, c) — a true no-op reorder.
        HttpResponseMessage patchResp = await client.PatchAsJsonAsync(
            $"/api/admin/venues/{venueId}/sections/reorder",
            new { sectionIds = new[] { a, b, c } });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        HttpResponseMessage getResp = await client.GetAsync($"/api/admin/venues/{venueId}/sections");
        JsonElement sections = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = sections.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(["A", "B", "C"], names);

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Sections.Where(s => s.Id == a).Select(s => s.SortOrder).SingleAsync());
        Assert.Equal(1, await db.Sections.Where(s => s.Id == b).Select(s => s.SortOrder).SingleAsync());
        Assert.Equal(2, await db.Sections.Where(s => s.Id == c).Select(s => s.SortOrder).SingleAsync());
    }
}
