using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUserActivities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "actor_user_id",
            table: "file_operations",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "user_activities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                activity_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                actor_device_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                target_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                target_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                target_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                owner_display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                parent_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                detail_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                source_parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_parent_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                destination_parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                destination_parent_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                resulting_file_version = table.Column<long>(type: "bigint", nullable: true),
                edit_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                recipient_display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                share_permission = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                share_action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                delete_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_activities", x => x.id);
                table.CheckConstraint("ck_user_activities_detail_shape", "(activity_type = 'UPLOAD'\n    AND resulting_file_version >= 1\n    AND source_parent_id IS NULL AND source_parent_name IS NULL\n    AND destination_parent_id IS NULL AND destination_parent_name IS NULL\n    AND edit_kind IS NULL AND recipient_user_id IS NULL\n    AND recipient_display_name IS NULL AND share_permission IS NULL\n    AND share_action IS NULL AND delete_kind IS NULL)\nOR\n(activity_type = 'MOVE'\n    AND source_parent_name IS NOT NULL AND destination_parent_name IS NOT NULL\n    AND resulting_file_version IS NULL AND edit_kind IS NULL\n    AND recipient_user_id IS NULL AND recipient_display_name IS NULL\n    AND share_permission IS NULL AND share_action IS NULL AND delete_kind IS NULL)\nOR\n(activity_type = 'EDIT'\n    AND resulting_file_version >= 1 AND edit_kind IS NOT NULL\n    AND source_parent_id IS NULL AND source_parent_name IS NULL\n    AND destination_parent_id IS NULL AND destination_parent_name IS NULL\n    AND recipient_user_id IS NULL AND recipient_display_name IS NULL\n    AND share_permission IS NULL AND share_action IS NULL AND delete_kind IS NULL)\nOR\n(activity_type = 'SHARE'\n    AND recipient_display_name IS NOT NULL\n    AND share_permission IS NOT NULL AND share_action IS NOT NULL\n    AND source_parent_id IS NULL AND source_parent_name IS NULL\n    AND destination_parent_id IS NULL AND destination_parent_name IS NULL\n    AND resulting_file_version IS NULL AND edit_kind IS NULL AND delete_kind IS NULL)\nOR\n(activity_type = 'DELETE'\n    AND delete_kind IS NOT NULL\n    AND source_parent_id IS NULL AND source_parent_name IS NULL\n    AND destination_parent_id IS NULL AND destination_parent_name IS NULL\n    AND resulting_file_version IS NULL AND edit_kind IS NULL\n    AND recipient_user_id IS NULL AND recipient_display_name IS NULL\n    AND share_permission IS NULL AND share_action IS NULL)");
                table.CheckConstraint("ck_user_activities_type_detail", "activity_type = detail_kind");
                table.ForeignKey(
                    name: "FK_user_activities_file_entries_destination_parent_id",
                    column: x => x.destination_parent_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_file_entries_parent_entry_id",
                    column: x => x.parent_entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_file_entries_source_parent_id",
                    column: x => x.source_parent_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_file_entries_target_entry_id",
                    column: x => x.target_entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_users_actor_user_id",
                    column: x => x.actor_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_user_activities_users_recipient_user_id",
                    column: x => x.recipient_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_file_operations_actor_user_id",
            table: "file_operations",
            column: "actor_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_activities_actor_occurred_id",
            table: "user_activities",
            columns: new[] { "actor_user_id", "occurred_at", "id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "IX_user_activities_destination_parent_id",
            table: "user_activities",
            column: "destination_parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_activities_owner_occurred_id",
            table: "user_activities",
            columns: new[] { "owner_user_id", "occurred_at", "id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "IX_user_activities_parent_entry_id",
            table: "user_activities",
            column: "parent_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_activities_recipient_user_id",
            table: "user_activities",
            column: "recipient_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_activities_source_parent_id",
            table: "user_activities",
            column: "source_parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_activities_target_occurred_id",
            table: "user_activities",
            columns: new[] { "target_entry_id", "occurred_at", "id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "ix_user_activities_type_occurred_id",
            table: "user_activities",
            columns: new[] { "activity_type", "occurred_at", "id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "ux_user_activities_operation_id",
            table: "user_activities",
            column: "operation_id",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_file_operations_users_actor_user_id",
            table: "file_operations",
            column: "actor_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_file_operations_users_actor_user_id",
            table: "file_operations");

        migrationBuilder.DropTable(
            name: "user_activities");

        migrationBuilder.DropIndex(
            name: "IX_file_operations_actor_user_id",
            table: "file_operations");

        migrationBuilder.DropColumn(
            name: "actor_user_id",
            table: "file_operations");
    }
}
