using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBackupReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "backup_decision",
            table: "upload_sessions",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "backup_expected_remote_file_id",
            table: "upload_sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "backup_expected_remote_file_version",
            table: "upload_sessions",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "backup_local_document_key",
            table: "upload_sessions",
            type: "character varying(36)",
            maxLength: 36,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "backup_relative_path",
            table: "upload_sessions",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "backup_source_checksum",
            table: "upload_sessions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "backup_source_modified_at",
            table: "upload_sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "backup_receipts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: false),
                local_document_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                remote_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                size = table.Column<long>(type: "bigint", nullable: false),
                source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                remote_file_version = table.Column<long>(type: "bigint", nullable: false),
                uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_backup_receipts", x => x.id);
                table.CheckConstraint("ck_backup_receipts_remote_version", "\"remote_file_version\" >= 1");
                table.CheckConstraint("ck_backup_receipts_size", "\"size\" >= 0");
                table.ForeignKey(
                    name: "FK_backup_receipts_devices_device_id",
                    column: x => x.device_id,
                    principalTable: "devices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_backup_receipts_file_entries_remote_file_id",
                    column: x => x.remote_file_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_backup_receipts_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_upload_sessions_active_backup_document",
            table: "upload_sessions",
            columns: new[] { "actor_user_id", "device_id", "backup_local_document_key" },
            unique: true,
            filter: "\"backup_local_document_key\" IS NOT NULL AND \"status\" IN ('ACTIVE', 'COMPLETING', 'RECOVERY_REQUIRED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_upload_sessions_backup_context",
            table: "upload_sessions",
            sql: "(\"backup_local_document_key\" IS NULL AND \"backup_relative_path\" IS NULL AND \"backup_source_modified_at\" IS NULL AND \"backup_source_checksum\" IS NULL AND \"backup_decision\" IS NULL AND \"backup_expected_remote_file_id\" IS NULL AND \"backup_expected_remote_file_version\" IS NULL) OR (\"backup_local_document_key\" IS NOT NULL AND \"backup_relative_path\" IS NOT NULL AND \"backup_source_modified_at\" IS NOT NULL AND ((\"backup_decision\" = 'NEW' AND \"backup_expected_remote_file_id\" IS NULL AND \"backup_expected_remote_file_version\" IS NULL) OR (\"backup_decision\" = 'CHANGED' AND \"backup_expected_remote_file_id\" IS NOT NULL AND \"backup_expected_remote_file_version\" >= 1)))");

        migrationBuilder.CreateIndex(
            name: "ix_backup_receipts_compare",
            table: "backup_receipts",
            columns: new[] { "user_id", "device_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_backup_receipts_device_id",
            table: "backup_receipts",
            column: "device_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_receipts_remote_file",
            table: "backup_receipts",
            column: "remote_file_id");

        migrationBuilder.CreateIndex(
            name: "ux_backup_receipts_user_device_document",
            table: "backup_receipts",
            columns: new[] { "user_id", "device_id", "local_document_key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backup_receipts");

        migrationBuilder.DropIndex(
            name: "ux_upload_sessions_active_backup_document",
            table: "upload_sessions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_upload_sessions_backup_context",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_decision",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_expected_remote_file_id",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_expected_remote_file_version",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_local_document_key",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_relative_path",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_source_checksum",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "backup_source_modified_at",
            table: "upload_sessions");
    }
}
