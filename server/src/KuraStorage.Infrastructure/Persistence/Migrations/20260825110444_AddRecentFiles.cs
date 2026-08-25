using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRecentFiles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "recent_files",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_id = table.Column<Guid>(type: "uuid", nullable: false),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recent_files", x => new { x.user_id, x.file_id });
                table.ForeignKey(
                    name: "FK_recent_files_file_entries_file_id",
                    column: x => x.file_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_recent_files_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_recent_files_file_id",
            table: "recent_files",
            column: "file_id");

        migrationBuilder.CreateIndex(
            name: "ix_recent_files_user_opened_at_file_id",
            table: "recent_files",
            columns: new[] { "user_id", "opened_at", "file_id" },
            descending: new[] { false, true, false });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "recent_files");
    }
}
