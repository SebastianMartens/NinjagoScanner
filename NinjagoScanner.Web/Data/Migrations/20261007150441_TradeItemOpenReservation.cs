using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NinjagoScanner.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TradeItemOpenReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradeItems_PhotoId",
                table: "TradeItems");

            migrationBuilder.AddColumn<bool>(
                name: "Reserved",
                table: "TradeItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Backfill: items of trades that are still open hold their reservation.
            migrationBuilder.Sql(
                "UPDATE \"TradeItems\" SET \"Reserved\" = 1 WHERE \"TradeId\" IN " +
                "(SELECT \"Id\" FROM \"Trades\" WHERE \"Status\" IN ('Pending', 'Executing'));");

            migrationBuilder.CreateIndex(
                name: "IX_TradeItems_PhotoId_OpenReservation",
                table: "TradeItems",
                column: "PhotoId",
                unique: true,
                filter: "\"Reserved\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradeItems_PhotoId_OpenReservation",
                table: "TradeItems");

            migrationBuilder.DropColumn(
                name: "Reserved",
                table: "TradeItems");

            migrationBuilder.CreateIndex(
                name: "IX_TradeItems_PhotoId",
                table: "TradeItems",
                column: "PhotoId");
        }
    }
}
