using System.Text.Json;
using System.Text.Json.Serialization;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.AspNetCore.RateLimiting;

namespace FourFoldAccountManager.Leaderboard.Service.Api;

public static class ParticipationEndpoints
{
    private static readonly JsonSerializerOptions InputJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void MapParticipationEndpoints(this WebApplication app)
    {
        app.MapPut("/v1/participation", HandleAsync)
            .RequireRateLimiting("participation");
    }

    private static async Task<IResult> HandleAsync(HttpRequest request, ILeaderboardStore store,
        TimeProvider clock, CancellationToken ct)
    {
        if (request.ContentLength is > LeaderboardRequestValidator.MaximumBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var body = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > LeaderboardRequestValidator.MaximumBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, count), ct);
        }

        ParticipationHeartbeat? heartbeat;
        try
        {
            heartbeat = JsonSerializer.Deserialize<ParticipationHeartbeat>(body.ToArray(), InputJson);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (!LeaderboardRequestValidator.IsValid(heartbeat)) return Results.BadRequest();

        await store.ApplyHeartbeatAsync(heartbeat!, clock.GetUtcNow(), ct);
        return Results.NoContent();
    }
}
