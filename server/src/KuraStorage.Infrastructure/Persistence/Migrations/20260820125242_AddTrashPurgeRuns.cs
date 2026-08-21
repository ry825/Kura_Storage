using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

public partial class AddTrashPurgeRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "trash_purge_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                examined_root_count = table.Column<int>(type: "integer", nullable: false),
                deleted_root_count = table.Column<int>(type: "integer", nullable: false),
                released_bytes = table.Column<long>(type: "bigint", nullable: false),
                error_count = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_trash_purge_runs", x => x.id);
                table.CheckConstraint(
                    "ck_trash_purge_runs_completion",
                    "(status = 'RUNNING' AND completed_at IS NULL) OR (status <> 'RUNNING' AND completed_at IS NOT NULL)");
                table.CheckConstraint(
                    "ck_trash_purge_runs_deleted",
                    "deleted_root_count >= 0 AND deleted_root_count <= examined_root_count");
                table.CheckConstraint("ck_trash_purge_runs_errors", "error_count >= 0");
                table.CheckConstraint("ck_trash_purge_runs_examined", "examined_root_count >= 0");
                table.CheckConstraint("ck_trash_purge_runs_released", "released_bytes >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "ix_trash_purge_runs_started_at",
            table: "trash_purge_runs",
            column: "started_at",
            descending: []);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "trash_purge_runs");
    }
}
