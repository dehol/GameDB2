using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoUpdate",
                table: "Alert",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutoUpdateMode",
                table: "Alert",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceLowest",
                table: "Alert",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShopId",
                table: "Alert",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alert_ShopId",
                table: "Alert",
                column: "ShopId");

            migrationBuilder.AddForeignKey(
                name: "FK_Alert_GameShop_ShopId",
                table: "Alert",
                column: "ShopId",
                principalTable: "GameShop",
                principalColumn: "ShopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alert_GameShop_ShopId",
                table: "Alert");

            migrationBuilder.DropIndex(
                name: "IX_Alert_ShopId",
                table: "Alert");

            migrationBuilder.DropColumn(
                name: "AutoUpdate",
                table: "Alert");

            migrationBuilder.DropColumn(
                name: "AutoUpdateMode",
                table: "Alert");

            migrationBuilder.DropColumn(
                name: "ReferenceLowest",
                table: "Alert");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "Alert");
        }
    }
}
