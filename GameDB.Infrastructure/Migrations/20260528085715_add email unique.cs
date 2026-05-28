using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addemailunique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_User_SteamId",
                table: "User");

            migrationBuilder.CreateIndex(
                name: "UQ_User_Email",
                table: "User",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_User_SteamId",
                table: "User",
                column: "SteamId",
                unique: true,
                filter: "\"SteamId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "UQ_User_SteamId",
                table: "User");

            migrationBuilder.CreateIndex(
                name: "UQ_User_SteamId",
                table: "User",
                column: "SteamId",
                unique: true);
        }
    }
}
