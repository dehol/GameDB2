using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.GameDB.Domain.Entities
{
    /// <summary>
    /// індекси для продуктивності каталогу.
    /// </summary>
    public partial class AddCatalogPerformanceIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm
            migrationBuilder.Sql(
                "CREATE EXTENSION IF NOT EXISTS pg_trgm;", 
                suppressTransaction: true);

            // Покриваючий індекс GameOffer
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_GameOffer_ExternalId_Cover"
                    ON public."GameOffer"("ExternalId" ASC, "FinalPrice" ASC NULLS LAST)
                    INCLUDE ("GameOfferId", "CurrentPrice", "CurrentDiscount", "Currency");
                """, suppressTransaction: true);

            // Покриваючий індекс GameExternalId(GameId)
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_GameExternalId_GameId_Cover"
                    ON public."GameExternalId"("GameId" ASC)
                    INCLUDE ("Id", "ShopId", "ExternalUrl");
                """, suppressTransaction: true);

            // Popularity sort
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_Popularity_Full"
                    ON public."Game"((COALESCE("Rating", 0.0) * COALESCE("RatingCount", 0)) DESC NULLS LAST)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            // ReleaseDate sort
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_ReleaseDate_Full"
                    ON public."Game"("ReleaseDate" DESC NULLS LAST)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            // Rating filter
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_Rating_Full"
                    ON public."Game"("Rating" DESC NULLS LAST)
                    WHERE "ImportStatus" = 'Full' AND "Rating" IS NOT NULL;
                """, suppressTransaction: true);

            // UpdatedAt sort
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_UpdatedAt_Full"
                    ON public."Game"("UpdatedAt" DESC)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            // Trigram GIN-індекс
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Game_Name_Trgm"
                    ON public."Game" USING gin("Name" gin_trgm_ops)
                    WHERE "ImportStatus" = 'Full';
                """, suppressTransaction: true);

            // PriceHistory composite index
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_PriceHistory_GameOfferId_RecordedAt"
                    ON public."PriceHistory"("GameOfferId" ASC, "RecordedAt" ASC)
                    INCLUDE ("Price", "DiscountPercent", "Currency");
                """, suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_PriceHistory_GameOfferId_RecordedAt";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_Name_Trgm";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_UpdatedAt_Full";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_Rating_Full";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_ReleaseDate_Full";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Game_Popularity_Full";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_GameExternalId_GameId_Cover";""", 
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_GameOffer_ExternalId_Cover";""", 
                suppressTransaction: true);
        }
    }
}