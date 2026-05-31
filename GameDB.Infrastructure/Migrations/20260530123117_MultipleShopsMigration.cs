using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultipleShopsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_Game_SteamAppId",
                table: "Game");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Game");

            migrationBuilder.DropColumn(
                name: "SteamAppId",
                table: "Game");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "GameShop",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "IconImage",
                table: "Game",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ImportStatus",
                table: "Game",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "GameExternalId",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    ShopId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    GameShopShopId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameExternalId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameExternalId_GameShop_GameShopShopId",
                        column: x => x.GameShopShopId,
                        principalTable: "GameShop",
                        principalColumn: "ShopId");
                    table.ForeignKey(
                        name: "FK_GameExternalId_GameShop_ShopId",
                        column: x => x.ShopId,
                        principalTable: "GameShop",
                        principalColumn: "ShopId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameExternalId_Game_GameId",
                        column: x => x.GameId,
                        principalTable: "Game",
                        principalColumn: "GameId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Game_ImportStatus",
                table: "Game",
                column: "ImportStatus");

            migrationBuilder.CreateIndex(
                name: "IX_GameExternalId_GameShopShopId",
                table: "GameExternalId",
                column: "GameShopShopId");

            migrationBuilder.CreateIndex(
                name: "UQ_GameExternalId_GameId_ShopId",
                table: "GameExternalId",
                columns: new[] { "GameId", "ShopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GameExternalId_ShopId_ExternalId",
                table: "GameExternalId",
                columns: new[] { "ShopId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameExternalId");

            migrationBuilder.DropIndex(
                name: "IX_Game_ImportStatus",
                table: "Game");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "GameShop");

            migrationBuilder.DropColumn(
                name: "ImportStatus",
                table: "Game");

            migrationBuilder.AlterColumn<string>(
                name: "IconImage",
                table: "Game",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Game",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SteamAppId",
                table: "Game",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Game_SteamAppId",
                table: "Game",
                column: "SteamAppId",
                unique: true);
        }
    }
}
