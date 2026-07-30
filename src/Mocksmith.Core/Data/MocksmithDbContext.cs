using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Data;

public class MocksmithDbContext(DbContextOptions<MocksmithDbContext> options) : DbContext(options)
{
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SampleTag> SampleTags => Set<SampleTag>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionPin> CollectionPins => Set<CollectionPin>();
    public DbSet<DraftSession> DraftSessions => Set<DraftSession>();
    public DbSet<DraftIteration> DraftIterations => Set<DraftIteration>();
    public DbSet<InputAsset> InputAssets => Set<InputAsset>();
    public DbSet<GenerationLog> GenerationLogs => Set<GenerationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.ToTable("Settings");
            entity.Property(s => s.DefaultModel).HasMaxLength(100);
            entity.HasData(new AppSettings { Id = 1, DefaultModel = "claude-sonnet-5" });
        });

        modelBuilder.Entity<Sample>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Summary).HasMaxLength(1000);
            entity.Property(s => s.SourceUrl).HasMaxLength(2000);
            entity.Property(s => s.HtmlFile).HasMaxLength(500);
            entity.Property(s => s.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<Variant>(entity =>
        {
            entity.Property(v => v.Name).HasMaxLength(100);
            entity.Property(v => v.HtmlFile).HasMaxLength(500);
            entity.HasIndex(v => new { v.SampleId, v.Name }).IsUnique();
            entity.HasOne(v => v.Sample)
                .WithMany(s => s.Variants)
                .HasForeignKey(v => v.SampleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(60);
            entity.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<SampleTag>(entity =>
        {
            entity.HasKey(st => new { st.SampleId, st.TagId });
            entity.HasOne(st => st.Sample)
                .WithMany(s => s.SampleTags)
                .HasForeignKey(st => st.SampleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(st => st.Tag)
                .WithMany(t => t.SampleTags)
                .HasForeignKey(st => st.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.TagQuery).HasMaxLength(500);
        });

        modelBuilder.Entity<CollectionPin>(entity =>
        {
            entity.HasKey(p => new { p.CollectionId, p.SampleId });
            entity.HasOne(p => p.Collection)
                .WithMany(c => c.Pins)
                .HasForeignKey(p => p.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Sample)
                .WithMany()
                .HasForeignKey(p => p.SampleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DraftIteration>(entity =>
        {
            entity.Property(i => i.HtmlFile).HasMaxLength(500);
            entity.Property(i => i.Model).HasMaxLength(100);
            entity.HasIndex(i => new { i.DraftSessionId, i.Index }).IsUnique();
            entity.HasOne(i => i.DraftSession)
                .WithMany(s => s.Iterations)
                .HasForeignKey(i => i.DraftSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InputAsset>(entity =>
        {
            entity.Property(a => a.FileName).HasMaxLength(260);
            entity.Property(a => a.FilePath).HasMaxLength(500);
            entity.Property(a => a.ContentType).HasMaxLength(100);
            entity.HasOne(a => a.DraftSession)
                .WithMany(s => s.Assets)
                .HasForeignKey(a => a.DraftSessionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(a => a.Sample)
                .WithMany(s => s.Assets)
                .HasForeignKey(a => a.SampleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GenerationLog>(entity =>
        {
            entity.Property(l => l.Model).HasMaxLength(100);
            entity.Property(l => l.Backend).HasMaxLength(20);
            entity.Property(l => l.EstimatedCostUsd).HasPrecision(10, 6);
        });

        modelBuilder.Entity<DraftSession>(entity =>
        {
            entity.Property(s => s.SourceUrl).HasMaxLength(2000);
            entity.Property(s => s.Model).HasMaxLength(100);
            entity.HasOne<Sample>()
                .WithMany()
                .HasForeignKey(s => s.SourceSampleId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
