using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMediaCleanupRuns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "media_cleanup_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                requested_by_admin_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                idempotency_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                request_fingerprint_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                worker_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                examined_count = table.Column<int>(type: "integer", nullable: false),
                deleted_count = table.Column<int>(type: "integer", nullable: false),
                released_bytes = table.Column<long>(type: "bigint", nullable: false),
                failure_count = table.Column<int>(type: "integer", nullable: false),
                remaining_cache_bytes = table.Column<long>(type: "bigint", nullable: true),
                failure_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_media_cleanup_runs", x => x.id);
                table.CheckConstraint("ck_media_cleanup_runs_counts", "examined_count >= 0 AND deleted_count >= 0 AND deleted_count <= examined_count AND failure_count >= 0 AND released_bytes >= 0 AND (remaining_cache_bytes IS NULL OR remaining_cache_bytes >= 0)");
                table.CheckConstraint("ck_media_cleanup_runs_lifecycle", "(status = 'PENDING' AND worker_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL) OR (status = 'RUNNING' AND worker_token IS NOT NULL AND lease_expires_at IS NOT NULL AND completed_at IS NULL) OR (status IN ('COMPLETED', 'FAILED') AND worker_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL)");
                table.CheckConstraint("ck_media_cleanup_runs_manual_identity", "(trigger = 'MANUAL' AND requested_by_admin_user_id IS NOT NULL AND idempotency_key_hash IS NOT NULL AND request_fingerprint_hash IS NOT NULL) OR (trigger = 'SCHEDULED' AND requested_by_admin_user_id IS NULL AND idempotency_key_hash IS NULL AND request_fingerprint_hash IS NULL)");
                table.ForeignKey(
                    name: "FK_media_cleanup_runs_users_requested_by_admin_user_id",
                    column: x => x.requested_by_admin_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_media_cleanup_runs_claim",
            table: "media_cleanup_runs",
            columns: new[] { "status", "lease_expires_at", "requested_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_media_cleanup_runs_latest",
            table: "media_cleanup_runs",
            columns: new[] { "requested_at", "id" },
            descending: new bool[0]);

        migrationBuilder.CreateIndex(
            name: "ux_media_cleanup_runs_active_scheduled",
            table: "media_cleanup_runs",
            column: "trigger",
            unique: true,
            filter: "trigger = 'SCHEDULED' AND status IN ('PENDING', 'RUNNING')");

        migrationBuilder.CreateIndex(
            name: "ux_media_cleanup_runs_manual_idempotency",
            table: "media_cleanup_runs",
            columns: new[] { "requested_by_admin_user_id", "idempotency_key_hash" },
            unique: true,
            filter: "trigger = 'MANUAL'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "media_cleanup_runs");
    }
}
