using System.Threading.RateLimiting;
using FourFoldAccountManager.Leaderboard.Service.Api;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var collection = new LeaderboardCollectionOptions();
builder.Configuration.GetSection(LeaderboardCollectionOptions.SectionName).Bind(collection);
builder.Services.AddSingleton(collection);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<LeaderboardDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Leaderboard") ??
                      "Host=127.0.0.1;Port=1;Database=unconfigured;Username=unconfigured;Password=unconfigured;Timeout=1"));
builder.Services.AddScoped<ILeaderboardStore, EfLeaderboardStore>();
builder.Services.AddSingleton<ILeaderboardPublicProfileSource>(services =>
    new FourFoldPublicProfileSource(services.GetRequiredService<LeaderboardCollectionOptions>()));
builder.Services.AddScoped<LeaderboardSamplingService>();
builder.Services.AddHostedService<LeaderboardSamplingWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("participation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientIp.GetPartitionKey(context,
                context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue<bool>("RateLimiting:TrustCloudflareConnectingIp")),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
app.UseRateLimiter();
app.MapParticipationEndpoints();
app.MapLeaderboardEndpoints();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", async (LeaderboardDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok()
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
app.Run();

public partial class Program;
