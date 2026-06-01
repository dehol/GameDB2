using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedNameToGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Game",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""Game""
                SET ""NormalizedName"" = trim(regexp_replace(
                    lower(regexp_replace(""Name"", '[®™©:–—''''""\.!\?,]', '', 'g')),
                    '[\s\-_]+', ' ', 'g'))");

            migrationBuilder.Sql(@"ALTER TABLE ""Game"" ALTER COLUMN ""NormalizedName"" DROP DEFAULT");

            migrationBuilder.CreateIndex(
                name: "IX_Game_NormalizedName",
                table: "Game",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Game_NormalizedName",
                table: "Game");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Game");
        }
    }
}
