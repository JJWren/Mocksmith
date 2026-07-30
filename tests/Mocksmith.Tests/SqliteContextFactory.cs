using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;

namespace Mocksmith.Tests;

/// <summary>
/// In-memory SQLite factory for service tests: one shared open connection so the
/// migrated schema survives across the contexts a service creates.
/// </summary>
public sealed class SqliteContextFactory : IDbContextFactory<MocksmithDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<MocksmithDbContext> _options;

    public SqliteContextFactory()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<MocksmithDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var context = CreateDbContext();
        context.Database.Migrate();
    }

    public MocksmithDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
