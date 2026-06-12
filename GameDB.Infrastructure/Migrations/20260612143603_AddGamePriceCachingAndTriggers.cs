using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.GameDB.Domain.Entities
{
    /// <inheritdoc />
    public partial class AddGamePriceCachingAndTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CachedBestDiscount",
                table: "Game",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "CachedBestPrice",
                table: "Game",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFree",
                table: "Game",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE public."Game" g
                SET 
                    "CachedBestPrice" = sub.best_price,
                    "CachedBestDiscount" = COALESCE(sub.best_discount, 0),
                    "IsFree" = (sub.best_price = 0)
                FROM (
                    SELECT e."GameId", MIN(o."FinalPrice") as best_price, MAX(o."CurrentDiscount") as best_discount
                    FROM public."GameOffer" o
                    JOIN public."GameExternalId" e ON o."ExternalId" = e."Id"
                    GROUP BY e."GameId"
                ) sub
                WHERE g."GameId" = sub."GameId";
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_CachedBestPrice_Full"
                    ON public."Game"("CachedBestPrice" ASC NULLS LAST)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_CachedBestDiscount_Full"
                    ON public."Game"("CachedBestDiscount" DESC)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.fn_sync_price_history()
                RETURNS TRIGGER AS $$
                DECLARE
                    v_latest_price    NUMERIC;
                    v_latest_id       INT;
                    v_game_id         INT;
                    v_best_price      NUMERIC;
                    v_best_discount   INT;
                BEGIN
                    -- Історія цін
                    SELECT "PriceHistory", "Price"
                      INTO v_latest_id, v_latest_price
                      FROM public."PriceHistory"
                     WHERE "GameOfferId" = NEW."GameOfferId"
                     ORDER BY "RecordedAt" DESC
                     LIMIT 1;

                    IF v_latest_id IS NULL THEN
                        INSERT INTO public."PriceHistory"
                            ("GameOfferId", "Price", "DiscountPercent", "Currency", "RecordedAt", "LastSyncedAt")
                        VALUES
                            (NEW."GameOfferId", NEW."CurrentPrice", NEW."CurrentDiscount", NEW."Currency", NOW(), NOW());
                    ELSIF v_latest_price IS DISTINCT FROM NEW."CurrentPrice"
                       OR (SELECT "DiscountPercent" FROM public."PriceHistory" WHERE "PriceHistory" = v_latest_id) IS DISTINCT FROM NEW."CurrentDiscount"
                    THEN
                        INSERT INTO public."PriceHistory"
                            ("GameOfferId", "Price", "DiscountPercent", "Currency", "RecordedAt", "LastSyncedAt")
                        VALUES
                            (NEW."GameOfferId", NEW."CurrentPrice", NEW."CurrentDiscount", NEW."Currency", NOW(), NOW());
                    ELSE
                        UPDATE public."PriceHistory" SET "LastSyncedAt" = NOW() WHERE "PriceHistory" = v_latest_id;
                    END IF;

                    -- Денормалізація цін у таблицю Game
                    SELECT "GameId" INTO v_game_id FROM public."GameExternalId" WHERE "Id" = NEW."ExternalId" LIMIT 1;

                    IF v_game_id IS NOT NULL THEN
                        SELECT MIN(o."FinalPrice"), MAX(o."CurrentDiscount")
                          INTO v_best_price, v_best_discount
                          FROM public."GameOffer" o
                          JOIN public."GameExternalId" e ON o."ExternalId" = e."Id"
                         WHERE e."GameId" = v_game_id;

                        UPDATE public."Game"
                           SET "CachedBestPrice" = v_best_price,
                               "CachedBestDiscount" = COALESCE(v_best_discount, 0),
                               "IsFree" = (v_best_price = 0)
                         WHERE "GameId" = v_game_id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_sync_price_history ON public."GameOffer";
                DROP TRIGGER IF EXISTS trg_sync_price_history_insert ON public."GameOffer";
                DROP TRIGGER IF EXISTS trg_sync_price_history_update ON public."GameOffer";

                CREATE TRIGGER trg_sync_price_history_insert
                    AFTER INSERT ON public."GameOffer"
                    FOR EACH ROW
                    EXECUTE FUNCTION public.fn_sync_price_history();

                CREATE TRIGGER trg_sync_price_history_update
                    AFTER UPDATE OF "CurrentPrice", "CurrentDiscount", "Currency" ON public."GameOffer"
                    FOR EACH ROW
                    WHEN (
                        OLD."CurrentPrice" IS DISTINCT FROM NEW."CurrentPrice" OR 
                        OLD."CurrentDiscount" IS DISTINCT FROM NEW."CurrentDiscount" OR 
                        OLD."Currency" IS DISTINCT FROM NEW."Currency"
                    )
                    EXECUTE FUNCTION public.fn_sync_price_history();
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS trg_sync_price_history_insert ON public."GameOffer";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS trg_sync_price_history_update ON public."GameOffer";""", suppressTransaction: true);
            
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_CachedBestPrice_Full";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_CachedBestDiscount_Full";""", suppressTransaction: true);

            migrationBuilder.DropColumn(name: "IsFree", table: "Game");
            migrationBuilder.DropColumn(name: "CachedBestDiscount", table: "Game");
            migrationBuilder.DropColumn(name: "CachedBestPrice", table: "Game");
        }
    }
}
