using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MakeImageLowPersistent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_media_jobs_queue",
            table: "media_jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_file_derivatives_cache_expiry",
            table: "file_derivatives");

        migrationBuilder.DropCheckConstraint(
            name: "ck_file_derivatives_thumbnail_expiry",
            table: "file_derivatives");

        migrationBuilder.AddColumn<string>(
            name: "origin",
            table: "media_jobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "INTERACTIVE_REPAIR");

        migrationBuilder.AddColumn<int>(
            name: "priority",
            table: "media_jobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_queue",
            table: "media_jobs",
            columns: new[] { "status", "priority", "available_at", "created_at", "id" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_media_jobs_origin",
            table: "media_jobs",
            sql: "origin IN ('INTERACTIVE_REPAIR', 'INGEST', 'BACKFILL')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_media_jobs_priority",
            table: "media_jobs",
            sql: "priority >= 0 AND priority <= 100");

        migrationBuilder.Sql(
            "UPDATE file_derivatives SET expires_at = NULL, last_accessed_at = NULL " +
            "WHERE derivative_type = 'IMAGE_LOW';");

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION apply_media_source_lifecycle() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.file_version <> OLD.file_version THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_CHANGED', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id
                      AND derivative.source_version <> NEW.file_version
                      AND job.derivative_id = derivative.id AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'DELETING', error_code = 'MEDIA_SOURCE_CHANGED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id AND source_version <> NEW.file_version
                      AND status <> 'DELETING';
                END IF;

                IF NEW.status = 'TRASHED' AND OLD.status <> 'TRASHED' THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_TRASHED', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'DELETING', error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND derivative_type IN ('IMAGE_MEDIUM', 'VIDEO_LOW', 'VIDEO_MEDIUM')
                      AND status <> 'DELETING';
                    UPDATE file_derivatives SET status = 'FAILED', error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL', 'IMAGE_LOW')
                      AND status IN ('PENDING', 'RUNNING');
                END IF;

                IF NEW.status = 'MISSING' AND OLD.status <> 'MISSING' THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_MISSING', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'BLOCKED_SOURCE_MISSING',
                        error_code = 'MEDIA_SOURCE_MISSING', revision = revision + 1,
                        updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id AND status <> 'DELETING';
                END IF;
                RETURN NEW;
            END;
            $$;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_derivatives_cache_expiry",
            table: "file_derivatives",
            sql: "derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL', 'IMAGE_LOW') OR status <> 'READY' OR (last_accessed_at IS NOT NULL AND expires_at > last_accessed_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_derivatives_persistent_expiry",
            table: "file_derivatives",
            sql: "derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL', 'IMAGE_LOW') OR (expires_at IS NULL AND last_accessed_at IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_media_jobs_queue",
            table: "media_jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_media_jobs_origin",
            table: "media_jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_media_jobs_priority",
            table: "media_jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_file_derivatives_cache_expiry",
            table: "file_derivatives");

        migrationBuilder.DropCheckConstraint(
            name: "ck_file_derivatives_persistent_expiry",
            table: "file_derivatives");

        migrationBuilder.DropColumn(
            name: "origin",
            table: "media_jobs");

        migrationBuilder.DropColumn(
            name: "priority",
            table: "media_jobs");

        migrationBuilder.Sql(
            "UPDATE file_derivatives SET last_accessed_at = updated_at, " +
            "expires_at = updated_at + INTERVAL '30 days' " +
            "WHERE derivative_type = 'IMAGE_LOW' AND status = 'READY';");

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION apply_media_source_lifecycle() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.file_version <> OLD.file_version THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_CHANGED', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id AND derivative.source_version <> NEW.file_version
                      AND job.derivative_id = derivative.id AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'DELETING', error_code = 'MEDIA_SOURCE_CHANGED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id AND source_version <> NEW.file_version AND status <> 'DELETING';
                END IF;
                IF NEW.status = 'TRASHED' AND OLD.status <> 'TRASHED' THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_TRASHED', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'DELETING', error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id
                      AND derivative_type IN ('IMAGE_LOW', 'IMAGE_MEDIUM', 'VIDEO_LOW', 'VIDEO_MEDIUM')
                      AND status <> 'DELETING';
                    UPDATE file_derivatives SET status = 'FAILED', error_code = 'MEDIA_SOURCE_TRASHED',
                        revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id AND derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL')
                      AND status IN ('PENDING', 'RUNNING');
                END IF;
                IF NEW.status = 'MISSING' AND OLD.status <> 'MISSING' THEN
                    UPDATE media_jobs AS job SET status = 'CANCELLED', worker_token = NULL,
                        heartbeat_at = NULL, completed_at = NEW.updated_at,
                        error_code = 'MEDIA_SOURCE_MISSING', updated_at = NEW.updated_at
                    FROM file_derivatives AS derivative
                    WHERE derivative.source_file_id = NEW.id AND job.derivative_id = derivative.id
                      AND job.status IN ('QUEUED', 'RUNNING');
                    UPDATE file_derivatives SET status = 'BLOCKED_SOURCE_MISSING',
                        error_code = 'MEDIA_SOURCE_MISSING', revision = revision + 1, updated_at = NEW.updated_at
                    WHERE source_file_id = NEW.id AND status <> 'DELETING';
                END IF;
                RETURN NEW;
            END;
            $$;
            """);

        migrationBuilder.CreateIndex(
            name: "ix_media_jobs_queue",
            table: "media_jobs",
            columns: new[] { "status", "available_at", "created_at", "id" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_derivatives_cache_expiry",
            table: "file_derivatives",
            sql: "derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR status <> 'READY' OR (last_accessed_at IS NOT NULL AND expires_at > last_accessed_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_derivatives_thumbnail_expiry",
            table: "file_derivatives",
            sql: "derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR (expires_at IS NULL AND last_accessed_at IS NULL)");
    }
}
