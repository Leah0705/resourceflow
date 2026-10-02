using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class HoldsControllerTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    private (int venueId, int sectionId, int resourceId) GetSeededIds()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();
        Section section = db.Sections.First(s => s.VenueId == venue.Id);
        Resource resource = db.Resources.First(t => t.SectionId == section.Id);
        return (venue.Id, section.Id, resource.Id);
    }

    [Fact]
    public async Task PlaceHold_ReturnsHoldId()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int sectionId, int resourceId) = GetSeededIds();

        var date = "2027-10-09T12:00:00"; // A Saturday, far enough ahead to avoid collision with relative-date tests

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date
        });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Failed with {response.StatusCode}: {err}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("holdId").GetString()));
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task PlaceHold_OnAlreadyHeldResource_ReturnsConflict()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int sectionId, int resourceId) = GetSeededIds();
        string date = DateTime.UtcNow.AddDays(101).ToString("yyyy-MM-ddT12:00:00");

        // Place first hold
        HttpResponseMessage first = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Place second hold on same resource+date
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ReleaseHold_Succeeds()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int sectionId, int resourceId) = GetSeededIds();

        HttpResponseMessage holdResp = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date = DateTime.UtcNow.AddDays(102).ToString("yyyy-MM-ddT12:00:00")
        });
        JsonElement holdBody = await holdResp.Content.ReadFromJsonAsync<JsonElement>();
        string? holdId = holdBody.GetProperty("holdId").GetString();

        HttpResponseMessage response = await client.DeleteAsync($"/api/holds/{holdId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseHold_ThenPlaceAgain_Succeeds()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int sectionId, int resourceId) = GetSeededIds();
        string date = DateTime.UtcNow.AddDays(103).ToString("yyyy-MM-ddT12:00:00");

        // Place hold
        HttpResponseMessage holdResp = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date
        });
        JsonElement holdBody = await holdResp.Content.ReadFromJsonAsync<JsonElement>();
        string? holdId = holdBody.GetProperty("holdId").GetString();

        // Release it
        await client.DeleteAsync($"/api/holds/{holdId}");

        // Place again on same resource+date
        HttpResponseMessage secondResp = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            sectionId,
            resourceId,
            date
        });

        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
    }

    [Fact]
    public async Task ReleaseHold_NonExistent_ReturnsNoContent()
    {
        HttpClient client = _factory.CreateClient();

        // Releasing a non-existent hold should still return 204 (safe to call)
        HttpResponseMessage response = await client.DeleteAsync("/api/holds/nonexistent-hold-id");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PlaceHold_WithCurrentHoldId_AtomicallyReplaces()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int sectionId, int resourceId) = GetSeededIds();
        string date1 = DateTime.UtcNow.AddDays(110).ToString("yyyy-MM-ddT12:00:00");
        string date2 = DateTime.UtcNow.AddDays(111).ToString("yyyy-MM-ddT12:00:00");

        // Place first hold
        HttpResponseMessage first = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId, sectionId, resourceId, date = date1
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        JsonElement firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        string? firstHoldId = firstBody.GetProperty("holdId").GetString();

        // Replace it atomically with a new hold on a different date
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId, sectionId, resourceId, date = date2, currentHoldId = firstHoldId
        });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        JsonElement secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(firstHoldId, secondBody.GetProperty("holdId").GetString());
    }

    [Fact]
    public async Task PlaceHold_InvalidModel_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new { venueId = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Auto-assign ("Any section") ───────────────────────────────────────────

    private (int venueId, int t1Id, int t2Id, int p1Id, int t1SectionId, int p1SectionId) GetCentralWorkspaceResourceIds()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue workspace = db.Venues.First(r => r.Name == "Central Workspace");
        Resource t1 = db.Resources.First(t => t.Name == "T1");
        Resource t2 = db.Resources.First(t => t.Name == "T2");
        Resource p1 = db.Resources.First(t => t.Name == "P1");
        return (workspace.Id, t1.Id, t2.Id, p1.Id, t1.SectionId, p1.SectionId);
    }

    [Fact]
    public async Task PlaceHold_AutoAssign_ReturnsHoldWithResolvedResource()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int t1Id, int t2Id, int p1Id, _, _) = GetCentralWorkspaceResourceIds();
        // Party of 2 → smallest fitting free resource is T2 (capacity 2).
        var date = DateTime.UtcNow.AddDays(120).ToString("yyyy-MM-ddT12:00:00");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            partySize = 2,
            date
            // resourceId/sectionId omitted → auto-assign
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("holdId").GetString()));
        Assert.Equal(t2Id, body.GetProperty("resourceId").GetInt32()); // resolved to T2
    }

    [Fact]
    public async Task PlaceHold_AutoAssign_Returns400_WhenPartySizeMissing()
    {
        HttpClient client = _factory.CreateClient();
        int venueId = GetCentralWorkspaceResourceIds().venueId;
        var date = DateTime.UtcNow.AddDays(121).ToString("yyyy-MM-ddT12:00:00");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            date
            // no partySize, no resourceId/sectionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceHold_AutoAssign_Returns409_WhenAllEligibleResourcesHeld()
    {
        HttpClient client = _factory.CreateClient();
        (int venueId, int t1Id, int t2Id, int p1Id, int t1SectionId, int p1SectionId) = GetCentralWorkspaceResourceIds();
        string date = DateTime.UtcNow.AddDays(122).ToString("yyyy-MM-ddT12:00:00");

        // Hold all resources that fit a party of 2 (T1, T2, P1 all fit 2) via explicit holds.
        // T1 and T2 share the Meeting Rooms section; P1 is in Studios.
        foreach ((int tid, int sid) in new[] { (t1Id, t1SectionId), (t2Id, t1SectionId), (p1Id, p1SectionId) })
        {
            HttpResponseMessage holdResp = await client.PostAsJsonAsync("/api/holds", new
            {
                venueId,
                resourceId = tid,
                sectionId = sid,
                date
            });
            Assert.Equal(HttpStatusCode.OK, holdResp.StatusCode);
        }

        // Now an auto-assign for a party of 2 should find no free candidate.
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            partySize = 2,
            date
        });

        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"Expected Conflict but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task PlaceHold_AutoAssign_Returns409_WhenNoResourceFitsPartySize()
    {
        HttpClient client = _factory.CreateClient();
        int venueId = GetCentralWorkspaceResourceIds().venueId;
        var date = DateTime.UtcNow.AddDays(123).ToString("yyyy-MM-ddT12:00:00");

        // No seeded resource fits a party of 100, so BuildCandidatesAsync returns zero
        // candidates before any hold is ever attempted.
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/holds", new
        {
            venueId,
            partySize = 100,
            date
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("No resources are available", body.GetProperty("message").GetString());
    }
}
