using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FourFoldAccountManager.Leaderboard.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_participation",
                columns: table => new
                {
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SharingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation_participation", x => x.InstallationId);
                });

            migrationBuilder.CreateTable(
                name: "player_sample_state",
                columns: table => new
                {
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSampledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NeedsBaseline = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_sample_state", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "xp_gain_event",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Gain = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xp_gain_event", x => x.Id);
                    table.CheckConstraint("ck_positive_gain", "\"Gain\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "installation_profile",
                columns: table => new
                {
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastActiveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation_profile", x => new { x.InstallationId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_installation_profile_installation_participation_Installatio~",
                        column: x => x.InstallationId,
                        principalTable: "installation_participation",
                        principalColumn: "InstallationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installation_profile_IsActive_LastActiveAtUtc",
                table: "installation_profile",
                columns: new[] { "IsActive", "LastActiveAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_installation_profile_PlayerId",
                table: "installation_profile",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_xp_gain_event_ObservedAtUtc_PlayerId",
                table: "xp_gain_event",
                columns: new[] { "ObservedAtUtc", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_profile");

            migrationBuilder.DropTable(
                name: "player_sample_state");

            migrationBuilder.DropTable(
                name: "xp_gain_event");

            migrationBuilder.DropTable(
                name: "installation_participation");
        }
    }
}
