using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CoHost.Server;

public sealed class DbHealthCheck(CoHostDbContext db, MigrationState state) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        bool canConnect;
        try
        {
            canConnect = await db.Database.CanConnectAsync(timeout.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            canConnect = false;
        }

        if (!canConnect)
        {
            return HealthCheckResult.Unhealthy("db unreachable");
        }

        return state.Applied ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("migrations pending");
    }
}
