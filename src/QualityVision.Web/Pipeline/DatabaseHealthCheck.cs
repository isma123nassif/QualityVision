using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using QualityVision.Infrastructure.Persistence;

namespace QualityVision.Web.Pipeline;

/// <summary>Unhealthy if the SQLite inspection database cannot be opened (the EF health-check package is not in CPM).</summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<InspectionDbContext> dbFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using InspectionDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("Datenbank erreichbar.")
                : HealthCheckResult.Unhealthy("Datenbank nicht erreichbar.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Datenbank nicht erreichbar.", ex);
        }
    }
}
