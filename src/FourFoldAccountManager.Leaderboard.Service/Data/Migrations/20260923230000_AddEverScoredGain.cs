using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FourFoldAccountManager.Leaderboard.Service.Data.Migrations;

[DbContext(typeof(LeaderboardDbContext))]
[Migration("20260923230000_AddEverScoredGain")]
public sealed partial class AddEverScoredGain : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "HasEverScoredGain",
            table: "player_sample_state",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.Sql("""
            UPDATE player_sample_state AS state
            SET "HasEverScoredGain" = true
            WHERE EXISTS (
                SELECT 1 FROM xp_gain_event AS gain
                WHERE gain."PlayerId" = state."PlayerId"
            )
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "HasEverScoredGain",
            table: "player_sample_state");
}
