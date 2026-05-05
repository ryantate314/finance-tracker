using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.HealthChecks;

public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("CanConnectAsync returned false");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
