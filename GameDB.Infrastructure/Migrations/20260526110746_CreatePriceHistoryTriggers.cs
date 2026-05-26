using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreatePriceHistoryTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Функція тригера для синхронізації PriceHistory
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.fn_sync_price_history()
                RETURNS TRIGGER AS $$
                DECLARE
                    v_latest_price  NUMERIC;
                    v_latest_id     INT;
                BEGIN
                    -- Беремо останній запис для цього оффера
                    SELECT ""PriceHistory"", ""Price""
                      INTO v_latest_id, v_latest_price
                      FROM public.""PriceHistory""
                     WHERE ""GameOfferId"" = NEW.""GameOfferId""
                     ORDER BY ""RecordedAt"" DESC
                     LIMIT 1;

                    IF v_latest_id IS NULL THEN
                        -- Перший запис для цього оффера → просто INSERT
                        INSERT INTO public.""PriceHistory""
                            (""GameOfferId"", ""Price"", ""DiscountPercent"", ""Currency"",
                             ""RecordedAt"", ""LastSyncedAt"")
                        VALUES
                            (NEW.""GameOfferId"", NEW.""CurrentPrice"", NEW.""CurrentDiscount"",
                             NEW.""Currency"", NOW(), NOW());

                    ELSIF v_latest_price IS DISTINCT FROM NEW.""CurrentPrice""
                       OR (SELECT ""DiscountPercent"" FROM public.""PriceHistory""
                           WHERE ""PriceHistory"" = v_latest_id) IS DISTINCT FROM NEW.""CurrentDiscount""
                    THEN
                        -- Ціна або знижка змінились → новий рядок
                        INSERT INTO public.""PriceHistory""
                            (""GameOfferId"", ""Price"", ""DiscountPercent"", ""Currency"",
                             ""RecordedAt"", ""LastSyncedAt"")
                        VALUES
                            (NEW.""GameOfferId"", NEW.""CurrentPrice"", NEW.""CurrentDiscount"",
                             NEW.""Currency"", NOW(), NOW());

                    ELSE
                        -- Ціна та сама → тільки оновлюємо LastSyncedAt in-place
                        UPDATE public.""PriceHistory""
                           SET ""LastSyncedAt"" = NOW()
                         WHERE ""PriceHistory"" = v_latest_id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            // Тригер на AFTER INSERT
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_sync_price_history_insert ON public.""GameOffer"";
                CREATE TRIGGER trg_sync_price_history_insert
                AFTER INSERT ON public.""GameOffer""
                FOR EACH ROW
                EXECUTE FUNCTION public.fn_sync_price_history();
            ");

            // Тригер на AFTER UPDATE (для CurrentPrice, CurrentDiscount, Currency)
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_sync_price_history_update ON public.""GameOffer"";
                CREATE TRIGGER trg_sync_price_history_update
                AFTER UPDATE OF ""CurrentPrice"", ""CurrentDiscount"", ""Currency"" ON public.""GameOffer""
                FOR EACH ROW
                WHEN (OLD.""CurrentPrice"" IS DISTINCT FROM NEW.""CurrentPrice""
                   OR OLD.""CurrentDiscount"" IS DISTINCT FROM NEW.""CurrentDiscount""
                   OR OLD.""Currency"" IS DISTINCT FROM NEW.""Currency"")
                EXECUTE FUNCTION public.fn_sync_price_history();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_sync_price_history_insert ON public.""GameOffer"";
                DROP TRIGGER IF EXISTS trg_sync_price_history_update ON public.""GameOffer"";
                DROP FUNCTION IF EXISTS public.fn_sync_price_history();
            ");
        }
    }
}
