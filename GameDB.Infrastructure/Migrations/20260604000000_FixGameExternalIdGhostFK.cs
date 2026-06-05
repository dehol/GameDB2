using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixGameExternalIdGhostFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Видаляємо паразитний FK і колонку GameShopShopId —
            // вони з'явились через .WithMany() без аргументу в AppDbContext.
            // FK_GameExternalId_GameShop_ShopId (правильний) вже існує з MultipleShopsMigration.
            migrationBuilder.DropForeignKey(
                name: "FK_GameExternalId_GameShop_GameShopShopId",
                table: "GameExternalId");

            migrationBuilder.DropIndex(
                name: "IX_GameExternalId_GameShopShopId",
                table: "GameExternalId");

            migrationBuilder.DropColumn(
                name: "GameShopShopId",
                table: "GameExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameShopShopId",
                table: "GameExternalId",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameExternalId_GameShopShopId",
                table: "GameExternalId",
                column: "GameShopShopId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameExternalId_GameShop_GameShopShopId",
                table: "GameExternalId",
                column: "GameShopShopId",
                principalTable: "GameShop",
                principalColumn: "ShopId");
        }
    }
}
