using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardRetentionWorker(
    IServiceScopeFactory scopeFactory,
    LeaderboardCollectionOptions options,
    TimeProvider clock,
    ILogger<LeaderboardRetentionWorker>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromDays(1);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ILeaderboardStore>();
                var now = clock.GetUtcNow();
                await store.DeleteGainEventsBeforeAsync(
                    now.AddDays(-options.RetentionDays), stoppingToken);
                await store.DeleteInactiveInstallationsBeforeAsync(now.AddDays(-40), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Leaderboard retention cleanup failed");
                delay = TimeSpan.FromMinutes(15);
            }

            try { await Task.Delay(delay, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
