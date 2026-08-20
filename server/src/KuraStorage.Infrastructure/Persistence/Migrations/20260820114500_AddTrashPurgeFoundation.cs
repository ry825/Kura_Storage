using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KuraStorageDbContext))]
[Migration("20260820114500_AddTrashPurgeFoundation")]
public sealed class AddTrashPurgeFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "actor_type",
            table: "audit_logs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "actor_device_id",
            table: "file_operations",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "request_id",
            table: "file_operations",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "trigger",
            table: "file_operations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE audit_logs
            SET actor_type = CASE
                WHEN actor_os_user IS NOT NULL THEN 'ADMIN_CLI'
                WHEN actor_user_id IS NOT NULL AND actor_device_id IS NOT NULL THEN 'USER_DEVICE'
                ELSE 'SYSTEM'
            END
            """);

        migrationBuilder.AlterColumn<string>(
            name: "actor_type",
            table: "audit_logs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "SYSTEM",
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_file_entries_trash_purge_candidates",
            table: "file_entries",
            columns: new[] { "status", "parent_id", "trashed_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_file_operations_incomplete_purge_target",
            table: "file_operations",
            column: "file_entry_id",
            unique: true,
            filter: "\"operation_type\" = 'PURGE' AND \"status\" IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')");

        migrationBuilder.CreateIndex(
            name: "ux_audit_logs_purge_success",
            table: "audit_logs",
            columns: new[] { "action", "target_id", "result_code" },
            unique: true,
            filter: "\"action\" IN ('FILE_PURGE_MANUAL', 'FILE_PURGE_RETENTION') AND \"result_code\" = 'SUCCESS'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_file_entries_trash_purge_candidates", table: "file_entries");
        migrationBuilder.DropIndex(name: "ux_file_operations_incomplete_purge_target", table: "file_operations");
        migrationBuilder.DropIndex(name: "ux_audit_logs_purge_success", table: "audit_logs");
        migrationBuilder.DropColumn(name: "actor_type", table: "audit_logs");
        migrationBuilder.DropColumn(name: "actor_device_id", table: "file_operations");
        migrationBuilder.DropColumn(name: "request_id", table: "file_operations");
        migrationBuilder.DropColumn(name: "trigger", table: "file_operations");
    }
}
