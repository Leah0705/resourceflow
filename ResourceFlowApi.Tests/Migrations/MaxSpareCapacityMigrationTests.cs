using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Migrations;

/// <summary>
/// Proves the AddVenueMaxSpareCapacity migration produces a correct nullable
/// INTEGER column both on a fresh install (Migrate() from empty) and on an upgrade from the
/// prior migration, and that the two schemas match exactly (the repo's migration-safety
/// invariant enforced by migration-check.yml).
/// </summary>
public class MaxSpareCapacityMigrationTests : IDisposable
{
    private const string LastMigrationBeforeOversize = "20260719220106_AddBookingSlotIntervalMinutes";

    private readonly SqliteConnection _connection;

    public MaxSpareCapacityMigrationTests()
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
        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task FreshInstall_CreatesNullableIntegerColumn()
    {
        using AppDbContext db = CreateContext();
        await db.Database.MigrateAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Venues);";
        using var reader = await cmd.ExecuteReaderAsync();

        bool foundColumn = false;
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1) == "MaxSpareCapacity")
            {
                foundColumn = true;
                Assert.Equal("INTEGER", reader.GetString(2));
                Assert.Equal(0L, reader.GetInt64(3)); // notnull = 0 → nullable (null = "off")
            }
        }

        Assert.True(foundColumn, "Venues.MaxSpareCapacity column should exist after a fresh Migrate().");
    }

    [Fact]
    public async Task Upgrade_ProducesSameSchema_AsFreshInstall()
    {
        // Path A: fresh install, all migrations at once.
        using var freshConnection = new SqliteConnection("Data Source=:memory:");
        freshConnection.Open();
        using (var freshDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(freshConnection).Options))
        {
            await freshDb.Database.MigrateAsync();
        }

        // Path B: upgrade — migrate to the last pre-oversize migration, then the rest.
        using AppDbContext upgradeDb = CreateContext();
        IMigrator migrator = upgradeDb.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(LastMigrationBeforeOversize);
        await migrator.MigrateAsync();

        Assert.Equal(GetVenuesSchema(freshConnection), GetVenuesSchema(_connection));
    }

    private static string GetVenuesSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Venues';";
        return (string)(cmd.ExecuteScalar() ?? throw new InvalidOperationException("Venues table not found."));
    }
}
