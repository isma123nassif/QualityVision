using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using QualityVision.Infrastructure.Persistence;

namespace QualityVision.Tests.Web;

/// <summary>In-memory SQLite shared through one open connection (the database lives as long as the connection).</summary>
internal sealed class InMemoryDb : IDbContextFactory<InspectionDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public InMemoryDb()
    {
        _connection.Open();
    }

    public InspectionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<InspectionDbContext>().UseSqlite(_connection).Options);

    public void EnsureCreated()
    {
        using InspectionDbContext db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();
}
