using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.AspNetCore.RateLimiting;

namespace FourFoldAccountManager.Leaderboard.Service.Api;

public static class LeaderboardEndpoints
{
    private static readonly TimeSpan MinimumStaleAfter = TimeSpan.FromMinutes(3);

    public static void MapLeaderboardEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/leaderboards/{period}", async (string period, HttpRequest request,
            ILeaderboardStore store, LeaderboardCollectionOptions options, TimeProvider clock,
            CancellationToken ct) =>
        {
            if (!LeaderboardRequestValidator.TryParsePeriod(period, out var parsed) ||
                !LeaderboardRequestValidator.TryParsePagination(request.Query["page"],
                    request.Query["pageSize"], out var page, out var size))
                return Results.BadRequest();

            var interval = options.MinimumSampleInterval;
            var staleAfter = interval > TimeSpan.Zero && interval <= TimeSpan.MaxValue / 3
                ? TimeSpan.FromTicks(Math.Max(MinimumStaleAfter.Ticks, interval.Ticks * 3))
                : MinimumStaleAfter;
            var result = await store.GetPageAsync(parsed, page, size, clock.GetUtcNow(), staleAfter, ct);
            return Results.Ok(result);
        }).RequireRateLimiting("public-read");
    }
}
