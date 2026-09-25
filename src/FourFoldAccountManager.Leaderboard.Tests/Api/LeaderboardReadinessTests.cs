using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Api;

public sealed class LeaderboardReadinessTests
{
    [Fact]
    public void StartupFailsWhenMigrationsCannotReachPostgreSql()
    {
        using var fixture = new ConnectedApiFactory(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1");

        Assert.ThrowsAny<Exception>(() => fixture.CreateClient());
    }

    private sealed class ConnectedApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Leaderboard"] = connectionString
                }));
    }
}
