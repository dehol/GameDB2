using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.GameDB.Domain.Entities
{
    /// <inheritdoc />
    public partial class FixShopNamesAndGogUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Виправлення назв магазинів ─────────────────────────────────
            // Причина: AddGogAndEpicShops використовував ON CONFLICT DO NOTHING,
            // тому якщо ShopId=2 вже існував з неправильною назвою (напр. "EpicGameStore"),
            // він залишився незміненим. Це виправлення примусово встановлює правильні значення.
            migrationBuilder.Sql(@"
                UPDATE ""GameShop""
                SET ""Name""    = 'GOG',
                    ""Slug""    = 'gog',
                    ""BaseUrl"" = 'https://www.gog.com',
                    ""ApiBaseUrl"" = 'https://api.gog.com'
                WHERE ""ShopId"" = 2;

                UPDATE ""GameShop""
                SET ""Name""    = 'Epic Games',
                    ""Slug""    = 'epic',
                    ""BaseUrl"" = 'https://store.epicgames.com',
                    ""ApiBaseUrl"" = 'https://api.egdata.app'
                WHERE ""ShopId"" = 3;
            ");

            // ── 2. Виправлення GOG ExternalUrl (числовий ID → slug) ───────────
            // Записи, імпортовані ДО цього фіксу, мають URL вигляду:
            //   https://www.gog.com/game/1207665503  (числовий ID — не працює)
            // Правильний формат:
            //   https://www.gog.com/game/{slug}      (наприклад /game/terraria)
            //
            // АВТОМАТИЧНЕ ВИПРАВЛЕННЯ: наступний запуск збагачення (Enrichment)
            // отримає slug з GOG API і автоматично оновить ExternalUrl через
            // StoreGameDetails.StoreUrl → EnrichSingleAsync.
            //
            // Тимчасово позначаємо GOG-записи зі «старим» URL як такі, що потребують
            // повторного збагачення (ImportStatus = Basic = 1), щоб Enrichment їх підхопив.
            // УВАГА: це скидає тільки GOG-записи, у яких URL містить числовий ID.
            migrationBuilder.Sql(@"
                UPDATE ""Game""
                SET ""ImportStatus"" = 1  -- Basic: потребує повторного збагачення
                WHERE ""GameId"" IN (
                    SELECT DISTINCT e.""GameId""
                    FROM ""GameExternalId"" e
                    WHERE e.""ShopId"" = 2                          -- GOG
                      AND e.""ExternalUrl"" ~ '/game/[0-9]+$'       -- URL з числовим ID
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Відкат назв магазинів до значень з попередньої міграції.
            // ExternalUrl не відкатуємо — втрата даних недоцільна.
            migrationBuilder.Sql(@"
                UPDATE ""GameShop""
                SET ""Name""    = 'GOG',
                    ""Slug""    = 'gog',
                    ""BaseUrl"" = 'https://www.gog.com',
                    ""ApiBaseUrl"" = 'https://api.gog.com'
                WHERE ""ShopId"" = 2;

                UPDATE ""GameShop""
                SET ""Name""    = 'Epic Games',
                    ""Slug""    = 'epic',
                    ""BaseUrl"" = 'https://store.epicgames.com',
                    ""ApiBaseUrl"" = 'https://api.egdata.app'
                WHERE ""ShopId"" = 3;
            ");
        }
    }
}
