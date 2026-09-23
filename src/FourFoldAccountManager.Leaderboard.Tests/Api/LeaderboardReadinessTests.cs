using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Api;

public sealed class LeaderboardReadinessTests
{
    [Fact]
    [Trait("RequiresDocker", "true")]
    public async Task ReadinessBecomesHealthyWhenPostgreSqlIsReachable()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        using var fixture = new ConnectedApiFactory(postgres.GetConnectionString());
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
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
