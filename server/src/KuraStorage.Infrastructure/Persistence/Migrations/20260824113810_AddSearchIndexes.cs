using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSearchIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Expression indexes are PostgreSQL-specific and are intentionally kept out of the EF model.
        // Suppressing the migration transaction allows CONCURRENTLY to avoid blocking writes on the
        // production file_entries table while each index is built.
        migrationBuilder.Sql(
            "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
            suppressTransaction: true);
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_file_entries_lower_name_trgm
            ON file_entries USING gin (lower(name) gin_trgm_ops)
            WHERE status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING');
            """,
            suppressTransaction: true);
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_file_entries_lower_name_prefix_id
            ON file_entries (lower(name) text_pattern_ops, id)
            WHERE status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING');
            """,
            suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX CONCURRENTLY IF EXISTS ix_file_entries_lower_name_prefix_id;",
            suppressTransaction: true);
        migrationBuilder.Sql(
            "DROP INDEX CONCURRENTLY IF EXISTS ix_file_entries_lower_name_trgm;",
            suppressTransaction: true);
        // pg_trgm may be shared by other features and is deliberately retained.
    }
}
