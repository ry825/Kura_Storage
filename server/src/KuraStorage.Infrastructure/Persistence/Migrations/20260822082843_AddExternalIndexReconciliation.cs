using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExternalIndexReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_file_entries_active_owner_parent_name",
            table: "file_entries");

        migrationBuilder.AlterColumn<string>(
            name: "status",
            table: "file_entries",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "missing_detected_at",
            table: "file_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "missing_last_checked_at",
            table: "file_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "missing_observation_id",
            table: "file_entries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "source_file_key",
            table: "file_entries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "source_modified_at",
            table: "file_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "source_observed_at",
            table: "file_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            table: "file_entries",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.CreateTable(
            name: "index_scan_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                enumerated_count = table.Column<int>(type: "integer", nullable: false),
                added_count = table.Column<int>(type: "integer", nullable: false),
                updated_count = table.Column<int>(type: "integer", nullable: false),
                moved_count = table.Column<int>(type: "integer", nullable: false),
                candidate_count = table.Column<int>(type: "integer", nullable: false),
                missing_count = table.Column<int>(type: "integer", nullable: false),
                revived_count = table.Column<int>(type: "integer", nullable: false),
                isolated_count = table.Column<int>(type: "integer", nullable: false),
                error_count = table.Column<int>(type: "integer", nullable: false),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_index_scan_runs", x => x.id);
                table.CheckConstraint("ck_index_scan_runs_completion", "(status = 'RUNNING' AND completed_at IS NULL) OR (status <> 'RUNNING' AND completed_at IS NOT NULL)");
                table.CheckConstraint("ck_index_scan_runs_counts", "enumerated_count >= 0 AND added_count >= 0 AND updated_count >= 0 AND moved_count >= 0 AND candidate_count >= 0 AND missing_count >= 0 AND revived_count >= 0 AND isolated_count >= 0 AND error_count >= 0");
                table.CheckConstraint("ck_index_scan_runs_error_code", "(status = 'FAILED' AND error_code IS NOT NULL) OR (status <> 'FAILED' AND error_code IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "index_scan_items",
            columns: table => new
            {
                scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                parent_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                entry_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                size = table.Column<long>(type: "bigint", nullable: false),
                mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                source_file_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                isolation_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_index_scan_items", x => new { x.scan_id, x.relative_path });
                table.CheckConstraint("ck_index_scan_items_size", "size >= 0");
                table.ForeignKey(
                    name: "FK_index_scan_items_index_scan_runs_scan_id",
                    column: x => x.scan_id,
                    principalTable: "index_scan_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_file_entries_missing_status_checked_at",
            table: "file_entries",
            columns: new[] { "status", "missing_last_checked_at", "id" },
            filter: "\"status\" IN ('MISSING_CANDIDATE', 'MISSING')");

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_managed_owner_parent_name",
            table: "file_entries",
            columns: new[] { "owner_user_id", "parent_id", "name" },
            unique: true,
            filter: "\"status\" IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')");

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_managed_owner_path",
            table: "file_entries",
            columns: new[] { "owner_user_id", "relative_path" },
            unique: true,
            filter: "\"status\" IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_entries_missing_metadata",
            table: "file_entries",
            sql: "(\"status\" IN ('ACTIVE', 'TRASHED') AND \"missing_detected_at\" IS NULL AND \"missing_last_checked_at\" IS NULL AND \"missing_observation_id\" IS NULL) OR (\"status\" IN ('MISSING_CANDIDATE', 'MISSING') AND \"parent_id\" IS NOT NULL AND \"missing_detected_at\" IS NOT NULL AND \"missing_last_checked_at\" IS NOT NULL AND \"missing_observation_id\" IS NOT NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_index_scan_items_scan_owner_parent",
            table: "index_scan_items",
            columns: new[] { "scan_id", "owner_user_id", "parent_relative_path" });

        migrationBuilder.CreateIndex(
            name: "ix_index_scan_items_scan_source_key",
            table: "index_scan_items",
            columns: new[] { "scan_id", "source_file_key" },
            filter: "source_file_key IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_index_scan_runs_started_at",
            table: "index_scan_runs",
            column: "started_at",
            descending: new bool[0]);

        migrationBuilder.CreateIndex(
            name: "ix_index_scan_runs_status_started_at",
            table: "index_scan_runs",
            columns: new[] { "status", "started_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM file_entries WHERE status IN ('MISSING_CANDIDATE', 'MISSING')) THEN
                    RAISE EXCEPTION 'Cannot roll back external indexing while missing file entries exist.'
                        USING ERRCODE = '23514';
                END IF;
            END $$;
            """);

        migrationBuilder.DropTable(
            name: "index_scan_items");

        migrationBuilder.DropTable(
            name: "index_scan_runs");

        migrationBuilder.DropIndex(
            name: "ix_file_entries_missing_status_checked_at",
            table: "file_entries");

        migrationBuilder.DropIndex(
            name: "ux_file_entries_managed_owner_parent_name",
            table: "file_entries");

        migrationBuilder.DropIndex(
            name: "ux_file_entries_managed_owner_path",
            table: "file_entries");

        migrationBuilder.DropCheckConstraint(
            name: "ck_file_entries_missing_metadata",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "missing_detected_at",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "missing_last_checked_at",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "missing_observation_id",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "source_file_key",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "source_modified_at",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "source_observed_at",
            table: "file_entries");

        migrationBuilder.DropColumn(
            name: "xmin",
            table: "file_entries");

        migrationBuilder.AlterColumn<string>(
            name: "status",
            table: "file_entries",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32);

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_active_owner_parent_name",
            table: "file_entries",
            columns: new[] { "owner_user_id", "parent_id", "name" },
            unique: true,
            filter: "\"status\" = 'ACTIVE'");
    }
}
