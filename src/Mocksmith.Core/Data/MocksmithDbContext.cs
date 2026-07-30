using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Data;

public class MocksmithDbContext(DbContextOptions<MocksmithDbContext> options) : DbContext(options)
{
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.ToTable("Settings");
            entity.Property(s => s.DefaultModel).HasMaxLength(100);
            entity.HasData(new AppSettings { Id = 1, DefaultModel = "claude-sonnet-5" });
        });
    }
}
