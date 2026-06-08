using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace GameDB.Infrastructure.GameDB.Domain.Entities
{
    /// <inheritdoc />
    public partial class AddGameEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "ImportStatus",
                table: "Game",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValueSql: "'Basic'::text",
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldMaxLength: 10,
                oldDefaultValueSql: "'Basic'::text");

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "Game",
                type: "vector(384)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Game_Embedding",
                table: "Game",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Game_Embedding",
                table: "Game");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Game");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AlterColumn<byte>(
                name: "ImportStatus",
                table: "Game",
                type: "smallint",
                maxLength: 10,
                nullable: false,
                defaultValueSql: "'Basic'::text",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValueSql: "'Basic'::text");
        }
    }
}
