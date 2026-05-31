using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePendingImportStatus2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ImportStatus",
                table: "Game",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Basic",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Basic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ImportStatus",
                table: "Game",
                type: "text",
                nullable: false,
                defaultValue: "Basic",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "Basic");
        }
    }
}
