using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardSamplingWorker(
    IServiceScopeFactory scopeFactory,
    LeaderboardCollectionOptions options,
    TimeProvider clock,
    ILogger<LeaderboardSamplingWorker>? logger = null) : BackgroundService
{
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
            await Task.Delay(options.MinimumSampleInterval, clock, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
