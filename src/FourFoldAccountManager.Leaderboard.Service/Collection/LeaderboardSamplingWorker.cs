using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardSamplingWorker(
    IServiceScopeFactory scopeFactory,
    LeaderboardCollectionOptions options,
    TimeProvider clock,
    LeaderboardSamplingSchedule schedule,
    ILogger<LeaderboardSamplingWorker>? logger = null) : BackgroundService
{
    // How often to look for players whose sample interval has elapsed when no profile has just started participating.
    internal static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CanCollect) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sampler = scope.ServiceProvider.GetRequiredService<LeaderboardSamplingService>();
                await sampler.RunOnceAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Leaderboard sampling pass failed");
            }
            await schedule.WaitForWorkAsync(IdlePollInterval, clock, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
