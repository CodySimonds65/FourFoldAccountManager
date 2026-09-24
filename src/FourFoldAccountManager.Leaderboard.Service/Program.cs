using System.Threading.RateLimiting;
using FourFoldAccountManager.Leaderboard.Service.Api;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var collection = new LeaderboardCollectionOptions();
builder.Configuration.GetSection(LeaderboardCollectionOptions.SectionName).Bind(collection);
if (collection.RetentionDays is < 31 or > 40)
    throw new InvalidOperationException("Collection:RetentionDays must be between 31 and 40 to retain a complete calendar month.");
if (collection.ActiveLeaseDuration != TimeSpan.FromMinutes(3))
    throw new InvalidOperationException("Collection:ActiveLeaseDuration must be three minutes.");
builder.Services.AddSingleton(collection);
var capacity = new LeaderboardCapacityOptions();
builder.Configuration.GetSection(LeaderboardCapacityOptions.SectionName).Bind(capacity);
if (capacity.MaxInstallations is < 1 or > 5_000 || capacity.MaxProfileLinks is < 1 or > 50_000)
    throw new InvalidOperationException("Capacity ceilings must be positive and may not exceed 5,000 installations or 50,000 profile links.");
builder.Services.AddSingleton(capacity);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<LeaderboardDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Leaderboard") ??
                      "Host=127.0.0.1;Port=1;Database=unconfigured;Username=unconfigured;Password=unconfigured;Timeout=1"));
builder.Services.AddScoped<ILeaderboardStore, EfLeaderboardStore>();
builder.Services.AddSingleton<ILeaderboardPublicProfileSource>(services =>
    new FourFoldPublicProfileSource(services.GetRequiredService<LeaderboardCollectionOptions>()));
builder.Services.AddScoped<LeaderboardSamplingService>();
builder.Services.AddHostedService<LeaderboardSamplingWorker>();
builder.Services.AddHostedService<LeaderboardRetentionWorker>();
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
    options.AddPolicy("public-read", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientIp.GetPartitionKey(context,
                context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue<bool>("RateLimiting:TrustCloudflareConnectingIp")),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LeaderboardDbContext>();
    await db.Database.MigrateAsync();
}

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
