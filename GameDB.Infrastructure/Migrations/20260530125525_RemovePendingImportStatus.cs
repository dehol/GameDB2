using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePendingImportStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ImportStatus",
                table: "Game",
                type: "text",
                nullable: false,
                defaultValue: "Basic",
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldDefaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "ImportStatus",
                table: "Game",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Basic");
        }
    }
}
