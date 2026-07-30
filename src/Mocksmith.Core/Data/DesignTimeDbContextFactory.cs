using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mocksmith.Core.Data;

/// <summary>
/// Lets `dotnet ef` create the context without booting the web app
/// (which fails fast when MOCKSMITH_* env vars are absent).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MocksmithDbContext>
{
    public MocksmithDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MocksmithDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new MocksmithDbContext(options);
    }
}
