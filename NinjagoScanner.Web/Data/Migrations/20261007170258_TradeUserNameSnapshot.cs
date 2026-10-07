using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NinjagoScanner.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TradeUserNameSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProposerUserName",
                table: "Trades",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecipientUserName",
                table: "Trades",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposerUserName",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "RecipientUserName",
                table: "Trades");
        }
    }
}
