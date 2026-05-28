using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddUserEmailUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Нормалізуємо існуючі Email до нижнього регістру перед додаванням індексу
        migrationBuilder.Sql(@"UPDATE ""User"" SET ""Email"" = LOWER(""Email"") WHERE ""Email"" IS NOT NULL;");

        migrationBuilder.CreateIndex(
            name: "UQ_User_Email",
            table: "User",
            column: "Email",
            unique: true,
            filter: "\"Email\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UQ_User_Email",
            table: "User");
    }
}
