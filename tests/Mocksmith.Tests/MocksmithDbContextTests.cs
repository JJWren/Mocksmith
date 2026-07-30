using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;

namespace Mocksmith.Tests;

public class MocksmithDbContextTests
{
    [Fact]
    public void Migrate_CreatesSchema_AndSeedsDefaultSettings()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MocksmithDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new MocksmithDbContext(options);
        context.Database.Migrate();

        var settings = context.Settings.Single();
        Assert.Equal(1, settings.Id);
        Assert.Equal("claude-sonnet-5", settings.DefaultModel);
    }
}
