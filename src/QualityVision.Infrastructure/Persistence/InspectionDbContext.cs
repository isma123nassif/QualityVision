using Microsoft.EntityFrameworkCore;

namespace QualityVision.Infrastructure.Persistence;

/// <summary>EF Core context for the inspection history (SQLite, single table, no migrations: demo trade-off).</summary>
/// <param name="options">Context options.</param>
public sealed class InspectionDbContext(DbContextOptions<InspectionDbContext> options) : DbContext(options)
{
    /// <summary>Persisted inspections.</summary>
    public DbSet<InspectionRecord> Inspections => Set<InspectionRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // SQLite cannot order DateTimeOffset natively: store UTC ticks as long.
        modelBuilder.Entity<InspectionRecord>(b =>
        {
            b.Property(static r => r.Timestamp).HasConversion(
                static v => v.UtcTicks,
                static v => new DateTimeOffset(v, TimeSpan.Zero));
            b.HasIndex(static r => r.Timestamp);
        });
    }
}
