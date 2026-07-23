using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFileOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                entry_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                size = table.Column<long>(type: "bigint", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                original_parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                original_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                trashed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                file_version = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_entries", x => x.id);
                table.CheckConstraint("ck_file_entries_file_version_positive", "\"file_version\" >= 1");
                table.CheckConstraint("ck_file_entries_size_nonnegative", "\"size\" >= 0");
                table.ForeignKey(
                    name: "FK_file_entries_file_entries_parent_id",
                    column: x => x.parent_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_entries_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "file_operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                operation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                file_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                target_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                expected_size = table.Column<long>(type: "bigint", nullable: true),
                expected_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_operations", x => x.id);
                table.ForeignKey(
                    name: "FK_file_operations_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_file_entries_owner_parent_status_updated_at",
            table: "file_entries",
            columns: new[] { "owner_user_id", "parent_id", "status", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_file_entries_parent_id",
            table: "file_entries",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_active_owner_parent_name",
            table: "file_entries",
            columns: new[] { "owner_user_id", "parent_id", "name" },
            unique: true,
            filter: "\"status\" = 'ACTIVE'");

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_active_owner_root",
            table: "file_entries",
            column: "owner_user_id",
            unique: true,
            filter: "\"parent_id\" IS NULL AND \"status\" = 'ACTIVE'");

        migrationBuilder.CreateIndex(
            name: "ux_file_entries_trashed_owner_path",
            table: "file_entries",
            columns: new[] { "owner_user_id", "relative_path" },
            unique: true,
            filter: "\"status\" = 'TRASHED'");

        migrationBuilder.CreateIndex(
            name: "ix_file_operations_file_entry_id",
            table: "file_operations",
            column: "file_entry_id");

        migrationBuilder.CreateIndex(
            name: "ix_file_operations_status_updated_at",
            table: "file_operations",
            columns: new[] { "status", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "ux_file_operations_owner_idempotency_key",
            table: "file_operations",
            columns: new[] { "owner_user_id", "idempotency_key" },
            unique: true,
            filter: "\"idempotency_key\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "file_entries");

        migrationBuilder.DropTable(
            name: "file_operations");
    }
}
