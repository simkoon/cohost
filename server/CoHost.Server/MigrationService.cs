using Microsoft.EntityFrameworkCore;

namespace CoHost.Server;

public sealed class MigrationState
{
    private volatile bool _applied;

    public bool Applied
    {
        get => _applied;
        set => _applied = value;
    }
}

/// <summary>
/// Applies EF migrations after Kestrel is listening, retrying until the database is reachable.
/// Running this before listen would keep the admin UI down whenever Postgres starts late (Pitfall 4).
/// </summary>
public sealed class MigrationService(IServiceProvider services, MigrationState state, ILogger<MigrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CoHostDbContext>();
                await db.Database.MigrateAsync(stoppingToken);
                state.Applied = true;
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Message only: the exception chain may echo connection details.
                logger.LogWarning("Database migration attempt {Attempt} failed: {Reason}", attempt, ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt * 2, 15)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
