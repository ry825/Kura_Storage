using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMediaDerivativeFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_derivatives",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_version = table.Column<long>(type: "bigint", nullable: false),
                derivative_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                profile_version = table.Column<int>(type: "integer", nullable: false),
                relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                size = table.Column<long>(type: "bigint", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                last_accessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                revision = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_derivatives", x => x.id);
                table.CheckConstraint("ck_file_derivatives_cache_expiry", "derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR status <> 'READY' OR (last_accessed_at IS NOT NULL AND expires_at > last_accessed_at)");
                table.CheckConstraint("ck_file_derivatives_failed_error", "status <> 'FAILED' OR error_code IS NOT NULL");
                table.CheckConstraint("ck_file_derivatives_profile_version", "profile_version >= 1");
                table.CheckConstraint("ck_file_derivatives_ready", "(status = 'READY' AND size > 0 AND relative_path IS NOT NULL) OR (status IN ('PENDING', 'RUNNING', 'FAILED') AND size = 0 AND relative_path IS NULL) OR (status IN ('BLOCKED_SOURCE_MISSING', 'DELETING') AND ((size = 0 AND relative_path IS NULL) OR (size > 0 AND relative_path IS NOT NULL)))");
                table.CheckConstraint("ck_file_derivatives_revision", "revision >= 1");
                table.CheckConstraint("ck_file_derivatives_size", "size >= 0");
                table.CheckConstraint("ck_file_derivatives_source_version", "source_version >= 1");
                table.CheckConstraint("ck_file_derivatives_status", "status IN ('PENDING', 'RUNNING', 'READY', 'FAILED', 'BLOCKED_SOURCE_MISSING', 'DELETING')");
                table.CheckConstraint("ck_file_derivatives_thumbnail_expiry", "derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR (expires_at IS NULL AND last_accessed_at IS NULL)");
                table.ForeignKey(
                    name: "FK_file_derivatives_file_entries_source_file_id",
                    column: x => x.source_file_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "derivative_leases",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                derivative_id = table.Column<Guid>(type: "uuid", nullable: false),
                lease_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                owner_token = table.Column<Guid>(type: "uuid", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_derivative_leases", x => x.id);
                table.CheckConstraint("ck_derivative_leases_type", "lease_type IN ('GENERATION', 'DELIVERY')");
                table.ForeignKey(
                    name: "FK_derivative_leases_file_derivatives_derivative_id",
                    column: x => x.derivative_id,
                    principalTable: "file_derivatives",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "media_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                derivative_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                worker_token = table.Column<Guid>(type: "uuid", nullable: true),
                heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                progress_percent = table.Column<int>(type: "integer", nullable: true),
                processed_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                total_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_media_jobs", x => x.id);
                table.CheckConstraint("ck_media_jobs_attempts", "attempt_count >= 0 AND attempt_count <= 3");
                table.CheckConstraint("ck_media_jobs_completion", "(status IN ('COMPLETED', 'FAILED', 'CANCELLED') AND completed_at IS NOT NULL) OR (status IN ('QUEUED', 'RUNNING') AND completed_at IS NULL)");
                table.CheckConstraint("ck_media_jobs_duration", "(processed_duration_ms IS NULL OR processed_duration_ms >= 0) AND (total_duration_ms IS NULL OR total_duration_ms >= 0) AND (processed_duration_ms IS NULL OR total_duration_ms IS NULL OR processed_duration_ms <= total_duration_ms)");
                table.CheckConstraint("ck_media_jobs_error", "status NOT IN ('FAILED', 'CANCELLED') OR error_code IS NOT NULL");
                table.CheckConstraint("ck_media_jobs_owner", "(status = 'RUNNING' AND worker_token IS NOT NULL AND heartbeat_at IS NOT NULL) OR (status <> 'RUNNING' AND worker_token IS NULL AND heartbeat_at IS NULL)");
                table.CheckConstraint("ck_media_jobs_progress", "progress_percent IS NULL OR (progress_percent >= 0 AND progress_percent <= 100)");
                table.CheckConstraint("ck_media_jobs_status", "status IN ('QUEUED', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED')");
                table.ForeignKey(
                    name: "FK_media_jobs_file_derivatives_derivative_id",
                    column: x => x.derivative_id,
                    principalTable: "file_derivatives",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_media_jobs_users_requested_by_user_id",
                    column: x => x.requested_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_derivative_leases_active",
            table: "derivative_leases",
            columns: new[] { "derivative_id", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_derivative_leases_expiry",
            table: "derivative_leases",
            columns: new[] { "expires_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_derivative_leases_owner",
            table: "derivative_leases",
            columns: new[] { "derivative_id", "lease_type", "owner_token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_file_derivatives_cleanup",
            table: "file_derivatives",
            columns: new[] { "status", "expires_at", "last_accessed_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_file_derivatives_source_status",
            table: "file_derivatives",
            columns: new[] { "source_file_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_file_derivatives_status_lease",
            table: "file_derivatives",
            columns: new[] { "status", "lease_until" });

        migrationBuilder.CreateIndex(
            name: "ix_file_derivatives_type_lru",
            table: "file_derivatives",
            columns: new[] { "derivative_type", "status", "last_accessed_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_file_derivatives_logical_key",
            table: "file_derivatives",
            columns: new[] { "source_file_id", "source_version", "derivative_type", "profile_version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_derivative",
            table: "media_jobs",
            column: "derivative_id");

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_history_cleanup",
            table: "media_jobs",
            columns: new[] { "status", "completed_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_queue",
            table: "media_jobs",
            columns: new[] { "status", "available_at", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_media_jobs_requested_by_user_id",
            table: "media_jobs",
            column: "requested_by_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_stale",
            table: "media_jobs",
            columns: new[] { "status", "heartbeat_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_media_jobs_active_derivative",
            table: "media_jobs",
            columns: new[] { "derivative_id", "status" },
            unique: true,
            filter: "status IN ('QUEUED', 'RUNNING')");

        migrationBuilder.Sql(
            """
            CREATE FUNCTION apply_media_source_lifecycle() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.file_version <> OLD.file_version THEN
                    UPDATE media_jobs AS job
                    SET status = 'CANCELLED',
                        worker_token = NULL,
                        heartbeat_at = NULL,
                        completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_CHANGED',
                        updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id
                      AND derivative.source_version <> NEW.file_version
                      AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');

                    UPDATE file_derivatives
                    SET status = 'DELETING',
                        error_code = 'MEDIA_SOURCE_CHANGED',
                        revision = revision + 1,
                        updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND source_version <> NEW.file_version
                      AND status <> 'DELETING';
                END IF;

                IF NEW.status = 'TRASHED' AND OLD.status <> 'TRASHED' THEN
                    UPDATE media_jobs AS job
                    SET status = 'CANCELLED',
                        worker_token = NULL,
                        heartbeat_at = NULL,
                        completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_TRASHED',
                        updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id
                      AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');

                    UPDATE file_derivatives
                    SET status = 'DELETING',
                        error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1,
                        updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND derivative_type IN ('IMAGE_LOW', 'IMAGE_MEDIUM', 'VIDEO_LOW', 'VIDEO_MEDIUM')
                      AND status <> 'DELETING';

                    UPDATE file_derivatives
                    SET status = 'FAILED',
                        error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1,
                        updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL')
                      AND status IN ('PENDING', 'RUNNING');
                END IF;

                IF NEW.status = 'MISSING' AND OLD.status <> 'MISSING' THEN
                    UPDATE media_jobs AS job
                    SET status = 'CANCELLED',
                        worker_token = NULL,
                        heartbeat_at = NULL,
                        completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_MISSING',
                        updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id
                      AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');

                    UPDATE file_derivatives
                    SET status = 'BLOCKED_SOURCE_MISSING',
                        error_code = 'MEDIA_SOURCE_MISSING',
                        revision = revision + 1,
                        updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND status <> 'DELETING';
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_file_entries_media_lifecycle
            AFTER UPDATE OF file_version, status ON file_entries
            FOR EACH ROW
            WHEN (OLD.file_version IS DISTINCT FROM NEW.file_version OR OLD.status IS DISTINCT FROM NEW.status)
            EXECUTE FUNCTION apply_media_source_lifecycle();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_file_entries_media_lifecycle ON file_entries;
            DROP FUNCTION IF EXISTS apply_media_source_lifecycle();
            """);

        migrationBuilder.DropTable(
            name: "derivative_leases");

        migrationBuilder.DropTable(
            name: "media_jobs");

        migrationBuilder.DropTable(
            name: "file_derivatives");
    }
}
