using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

public class DbSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DbSeederTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void Seed_CreatesVenues_WhenDbIsEmpty()
    {
        using AppDbContext db = CreateContext();

        DbSeeder.Seed(db);

        var venues = db.Venues.ToList();
        Assert.Equal(2, venues.Count);
        Assert.Contains(venues, r => r.Name == "Central Workspace");
        Assert.Contains(venues, r => r.Name == "Harbour Studio");
    }

    [Fact]
    public void Seed_IsIdempotent_CallingTwiceDoesNotDuplicate()
    {
        using AppDbContext db = CreateContext();

        DbSeeder.Seed(db);
        DbSeeder.Seed(db);

        var venues = db.Venues.ToList();
        Assert.Equal(2, venues.Count);
    }

    [Fact]
    public void Seed_CreatesCorrectSectionsAndResources()
    {
        using AppDbContext db = CreateContext();

        DbSeeder.Seed(db);

        Venue centralWorkspace = db.Venues
            .Include(r => r.Sections)
            .ThenInclude(s => s.Resources)
            .First(r => r.Name == "Central Workspace");

        Assert.Equal(2, centralWorkspace.Sections.Count);

        Section meetingRooms = centralWorkspace.Sections.First(s => s.Name == "Meeting Rooms");
        Assert.Equal(2, meetingRooms.Resources.Count);
        Assert.Contains(meetingRooms.Resources, t => t.Name == "T1" && t.Capacity == 4);
        Assert.Contains(meetingRooms.Resources, t => t.Name == "T2" && t.Capacity == 2);

        Section studios = centralWorkspace.Sections.First(s => s.Name == "Studios");
        Assert.Single(studios.Resources);
        Assert.Contains(studios.Resources, t => t.Name == "P1" && t.Capacity == 4);

        Venue harbourStudio = db.Venues
            .Include(r => r.Sections)
            .ThenInclude(s => s.Resources)
            .First(r => r.Name == "Harbour Studio");

        Assert.Single(harbourStudio.Sections);

        Section workspaces = harbourStudio.Sections.First(s => s.Name == "Workspaces");
        Assert.Equal(2, workspaces.Resources.Count);
        Assert.Contains(workspaces.Resources, t => t.Name == "B1" && t.Capacity == 2);
        Assert.Contains(workspaces.Resources, t => t.Name == "B2" && t.Capacity == 2);
    }
}
