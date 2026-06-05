using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.GameDB.Domain.Entities
{
    /// <inheritdoc />
    public partial class AddGogAndEpicShops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: змінено DO NOTHING → DO UPDATE SET, щоб назви магазинів
            //      виправлялись навіть якщо запис з таким ShopId вже існував
            //      (наприклад, якщо хтось вручну вставив рядок з неправильною назвою).
            migrationBuilder.Sql(@"
                INSERT INTO ""GameShop"" (""ShopId"", ""Name"", ""Slug"", ""BaseUrl"", ""ApiBaseUrl"")
                OVERRIDING SYSTEM VALUE
                VALUES
                    (2, 'GOG',        'gog',  'https://www.gog.com',         'https://api.gog.com'),
                    (3, 'Epic Games', 'epic', 'https://store.epicgames.com', 'https://api.egdata.app')
                ON CONFLICT (""ShopId"") DO UPDATE SET
                    ""Name""       = EXCLUDED.""Name"",
                    ""Slug""       = EXCLUDED.""Slug"",
                    ""BaseUrl""    = EXCLUDED.""BaseUrl"",
                    ""ApiBaseUrl"" = EXCLUDED.""ApiBaseUrl"";

                SELECT setval(
                    pg_get_serial_sequence('""GameShop""', 'ShopId'),
                    GREATEST((SELECT MAX(""ShopId"") FROM ""GameShop""), 3)
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""GameShop"" WHERE ""ShopId"" IN (2, 3);
            ");
        }
    }
}
