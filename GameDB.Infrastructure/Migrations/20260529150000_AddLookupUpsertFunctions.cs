using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameDB.Infrastructure.Migrations;

public partial class AddLookupUpsertFunctions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.fn_upsert_developer(p_name text)
            RETURNS integer
            LANGUAGE sql
            AS $$
                INSERT INTO "Developer" ("Name") VALUES (p_name)
                ON CONFLICT ("Name") DO UPDATE SET "Name" = EXCLUDED."Name"
                RETURNING "DeveloperId";
            $$;

            CREATE OR REPLACE FUNCTION public.fn_upsert_publisher(p_name text)
            RETURNS integer
            LANGUAGE sql
            AS $$
                INSERT INTO "Publisher" ("Name") VALUES (p_name)
                ON CONFLICT ("Name") DO UPDATE SET "Name" = EXCLUDED."Name"
                RETURNING "PublisherId";
            $$;

            CREATE OR REPLACE FUNCTION public.fn_upsert_genre(p_name text)
            RETURNS integer
            LANGUAGE sql
            AS $$
                INSERT INTO "Genre" ("Name") VALUES (p_name)
                ON CONFLICT ("Name") DO UPDATE SET "Name" = EXCLUDED."Name"
                RETURNING "GenreId";
            $$;

            CREATE OR REPLACE FUNCTION public.fn_upsert_tag(p_name text)
            RETURNS integer
            LANGUAGE sql
            AS $$
                INSERT INTO "Tag" ("Name") VALUES (p_name)
                ON CONFLICT ("Name") DO UPDATE SET "Name" = EXCLUDED."Name"
                RETURNING "TagId";
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS public.fn_upsert_tag(text);
            DROP FUNCTION IF EXISTS public.fn_upsert_genre(text);
            DROP FUNCTION IF EXISTS public.fn_upsert_publisher(text);
            DROP FUNCTION IF EXISTS public.fn_upsert_developer(text);
            """);
    }
}
