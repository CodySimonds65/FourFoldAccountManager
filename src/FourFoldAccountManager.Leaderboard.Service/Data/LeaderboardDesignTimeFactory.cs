using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class LeaderboardDesignTimeFactory : IDesignTimeDbContextFactory<LeaderboardDbContext>
{
    public LeaderboardDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LeaderboardDbContext>()
            .UseNpgsql("Host=localhost;Database=leaderboard_design")
            .Options;
        return new LeaderboardDbContext(options);
    }
}
