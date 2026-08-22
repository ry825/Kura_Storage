using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUploadSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "upload_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                file_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                expected_size = table.Column<long>(type: "bigint", nullable: false),
                expected_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                received_bytes = table.Column<long>(type: "bigint", nullable: false),
                last_chunk_offset = table.Column<long>(type: "bigint", nullable: true),
                last_chunk_length = table.Column<long>(type: "bigint", nullable: true),
                last_chunk_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                temporary_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cleaned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_upload_sessions", x => x.id);
                table.CheckConstraint("ck_upload_sessions_byte_range", "\"expected_size\" >= 0 AND \"received_bytes\" >= 0 AND \"received_bytes\" <= \"expected_size\"");
                table.CheckConstraint("ck_upload_sessions_expiration", "\"expires_at\" <= \"absolute_expires_at\"");
                table.ForeignKey(
                    name: "FK_upload_sessions_devices_device_id",
                    column: x => x.device_id,
                    principalTable: "devices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_upload_sessions_file_entries_destination_folder_id",
                    column: x => x.destination_folder_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_upload_sessions_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_upload_sessions_cleanup_candidates",
            table: "upload_sessions",
            columns: new[] { "status", "expires_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_upload_sessions_destination_folder_id",
            table: "upload_sessions",
            column: "destination_folder_id");

        migrationBuilder.CreateIndex(
            name: "ix_upload_sessions_device_status",
            table: "upload_sessions",
            columns: new[] { "device_id", "status", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_upload_sessions_owner_status",
            table: "upload_sessions",
            columns: new[] { "owner_user_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ux_upload_sessions_file_operation_id",
            table: "upload_sessions",
            column: "file_operation_id",
            unique: true,
            filter: "\"file_operation_id\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_upload_sessions_owner_idempotency_key",
            table: "upload_sessions",
            columns: new[] { "owner_user_id", "idempotency_key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "upload_sessions");
    }
}
