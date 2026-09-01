using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTextFileVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "previous_file_version",
            table: "file_operations",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "result_file_version",
            table: "file_operations",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "version_content_relative_path",
            table: "file_operations",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "version_publish_stage",
            table: "file_operations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "version_sha256",
            table: "file_operations",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "version_temporary_relative_path",
            table: "file_operations",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "file_version_records",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                file_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                size = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                content_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                change_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_version_records", x => x.id);
                table.CheckConstraint("ck_file_version_records_sha256_lower_hex", "\"sha256\" ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_file_version_records_size_bounded", "\"size\" >= 0 AND \"size\" <= 1048576");
                table.CheckConstraint("ck_file_version_records_version_positive", "\"version\" >= 1");
                table.ForeignKey(
                    name: "FK_file_version_records_devices_actor_device_id",
                    column: x => x.actor_device_id,
                    principalTable: "devices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_file_version_records_file_entries_file_entry_id",
                    column: x => x.file_entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_version_records_users_actor_user_id",
                    column: x => x.actor_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_file_version_records_actor_device_id",
            table: "file_version_records",
            column: "actor_device_id");

        migrationBuilder.CreateIndex(
            name: "ix_file_version_records_actor_user_id",
            table: "file_version_records",
            column: "actor_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_file_version_records_file_created_id",
            table: "file_version_records",
            columns: new[] { "file_entry_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_file_version_records_file_version",
            table: "file_version_records",
            columns: new[] { "file_entry_id", "version" },
            unique: true,
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "file_version_records");

        migrationBuilder.DropColumn(
            name: "previous_file_version",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "result_file_version",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "version_content_relative_path",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "version_publish_stage",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "version_sha256",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "version_temporary_relative_path",
            table: "file_operations");
    }
}
