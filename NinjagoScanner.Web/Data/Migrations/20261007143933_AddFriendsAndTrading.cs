using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NinjagoScanner.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendsAndTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionSharingSettings",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionSharingSettings", x => x.CollectionId);
                });

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RequesterUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AddresseeUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UserLowId = table.Column<string>(type: "TEXT", nullable: false),
                    UserHighId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradeId = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProposerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ProposerUserName = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientUserName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProposerCollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientCollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    ProposerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExecutingStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeLogItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradeLogEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesName = table.Column<string>(type: "TEXT", nullable: false),
                    CardNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CardName = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeLogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeLogItems_TradeLogEntries_TradeLogEntryId",
                        column: x => x.TradeLogEntryId,
                        principalTable: "TradeLogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradeId = table.Column<string>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    PhotoId = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesName = table.Column<string>(type: "TEXT", nullable: false),
                    CardNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CardName = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeItems_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_AddresseeUserId",
                table: "Friendships",
                column: "AddresseeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_RequesterUserId",
                table: "Friendships",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_UserLowId_UserHighId",
                table: "Friendships",
                columns: new[] { "UserLowId", "UserHighId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeItems_PhotoId",
                table: "TradeItems",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeItems_TradeId",
                table: "TradeItems",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeLogEntries_TradeId",
                table: "TradeLogEntries",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeLogItems_TradeLogEntryId",
                table: "TradeLogItems",
                column: "TradeLogEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ProposerUserId",
                table: "Trades",
                column: "ProposerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_RecipientUserId",
                table: "Trades",
                column: "RecipientUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionSharingSettings");

            migrationBuilder.DropTable(
                name: "Friendships");

            migrationBuilder.DropTable(
                name: "TradeItems");

            migrationBuilder.DropTable(
                name: "TradeLogItems");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TradeLogEntries");
        }
    }
}
