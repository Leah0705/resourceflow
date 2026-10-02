using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class VenuesControllerTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    [Fact]
    public async Task GetAll_ReturnsSeededVenues()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/venues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement venues = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(venues.GetArrayLength() >= 1);

        // Check that at least one venue has a name
        foreach (JsonElement r in venues.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(r.GetProperty("name").GetString()));
        }
    }

    [Fact]
    public async Task GetById_ReturnsVenueWithSectionsAndResources()
    {
        HttpClient client = _factory.CreateClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int venueId = db.Venues.First().Id;

        HttpResponseMessage response = await client.GetAsync($"/api/venues/{venueId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("name").GetString()));

        JsonElement sections = body.GetProperty("sections");
        Assert.True(sections.GetArrayLength() >= 1);

        JsonElement resources = sections[0].GetProperty("resources");
        Assert.True(resources.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/venues/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateVenue_WithoutAuth_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/venues", new
        {
            name = "Unauthorized Venue",
            sections = new[] { new { name = "S1", resources = Array.Empty<object>() } }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateVenue_WithAuth_Returns201()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/venues", new
        {
            name = "New Venue",
            address = "456 New St",
            sections = new[]
            {
                new
                {
                    name = "Studio",
                    resources = new[]
                    {
                        new { name = "X1", capacity = 6 }
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New Venue", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
    }

    [Fact]
    public async Task AddSection_WithAuth_Succeeds()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int venueId = db.Venues.First().Id;

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/venues/{venueId}/sections", new
        {
            name = "VIP Section"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VIP Section", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
    }

    [Fact]
    public async Task AddResource_WithAuth_Succeeds()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();
        Section section = db.Sections.First(s => s.VenueId == venue.Id);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/venues/{venue.Id}/sections/{section.Id}/resources", new
            {
                name = "NewResource",
                capacity = 8
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NewResource", body.GetProperty("name").GetString());
        Assert.Equal(8, body.GetProperty("capacity").GetInt32());
    }

    [Fact]
    public async Task UpdateVenue_WithAuth_Succeeds()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int venueId = db.Venues.First().Id;

        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/venues/{venueId}", new
        {
            name = "Updated Venue",
            address = "789 Updated St",
            openTime = "10:00",
            closeTime = "23:00"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Venue", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateVenue_ReturnsBadRequest_ForInvalidBookingDuration()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int venueId = db.Venues.First().Id;

        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/venues/{venueId}", new
        {
            name = "Still A Valid Name",
            defaultBookingDurationMinutes = 999,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("DefaultBookingDurationMinutes", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateSection_WithAuth_Succeeds()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();
        Section section = db.Sections.First(s => s.VenueId == venue.Id);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/venues/{venue.Id}/sections/{section.Id}", new
            {
                name = "Updated Section"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Section", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateResource_WithAuth_Succeeds()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();
        Section section = db.Sections.First(s => s.VenueId == venue.Id);
        Resource resource = db.Resources.First(t => t.SectionId == section.Id);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/venues/{venue.Id}/sections/{section.Id}/resources/{resource.Id}", new
            {
                name = "UpdatedT1",
                capacity = 6
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UpdatedT1", body.GetProperty("name").GetString());
        Assert.Equal(6, body.GetProperty("capacity").GetInt32());
    }

    [Fact]
    public async Task DeleteResource_WithAuth_ReturnsNoContent()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        // First, add a resource to delete
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();
        Section section = db.Sections.First(s => s.VenueId == venue.Id);

        HttpResponseMessage addResp = await client.PostAsJsonAsync(
            $"/api/venues/{venue.Id}/sections/{section.Id}/resources", new
            {
                name = "ToDelete",
                capacity = 2
            });
        JsonElement addedResource = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        int resourceId = addedResource.GetProperty("id").GetInt32();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/venues/{venue.Id}/sections/{section.Id}/resources/{resourceId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSection_WithAuth_ReturnsNoContent()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Venue venue = db.Venues.First();

        // Add a section to delete
        HttpResponseMessage addResp = await client.PostAsJsonAsync(
            $"/api/venues/{venue.Id}/sections", new
            {
                name = "TempSection"
            });
        JsonElement addedSection = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        int sectionId = addedSection.GetProperty("id").GetInt32();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/venues/{venue.Id}/sections/{sectionId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSection_NonExistent_Returns404()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/venues/1/sections/9999", new
        {
            name = "Doesn't Matter"
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSection_NonExistent_Returns404()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.DeleteAsync("/api/venues/1/sections/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateResource_NonExistent_Returns404()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/venues/1/sections/1/resources/9999", new
        {
            name = "Doesn't Matter",
            capacity = 4
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteResource_NonExistent_Returns404()
    {
        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.DeleteAsync("/api/venues/1/sections/1/resources/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
