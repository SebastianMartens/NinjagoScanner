using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NinjagoScanner.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGamification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AchievementUnlocks",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    AchievementId = table.Column<string>(type: "TEXT", nullable: false),
                    UnlockedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementUnlocks", x => new { x.CollectionId, x.AchievementId });
                });

            migrationBuilder.CreateTable(
                name: "GamificationProfiles",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    BonusXp = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedBackgroundId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamificationProfiles", x => x.CollectionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AchievementUnlocks");

            migrationBuilder.DropTable(
                name: "GamificationProfiles");
        }
    }
}
