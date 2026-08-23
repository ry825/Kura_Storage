using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFileSharing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_upload_sessions_users_owner_user_id",
            table: "upload_sessions");

        migrationBuilder.DropIndex(
            name: "ix_upload_sessions_owner_status",
            table: "upload_sessions");

        migrationBuilder.DropIndex(
            name: "ux_upload_sessions_owner_idempotency_key",
            table: "upload_sessions");

        migrationBuilder.RenameColumn(
            name: "owner_user_id",
            table: "upload_sessions",
            newName: "actor_user_id");

        migrationBuilder.AddColumn<Guid>(
            name: "target_owner_user_id",
            table: "upload_sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE upload_sessions
            SET target_owner_user_id = actor_user_id
            WHERE target_owner_user_id IS NULL
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "target_owner_user_id",
            table: "upload_sessions",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "shares",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                target_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_shares", x => x.id);
                table.ForeignKey(
                    name: "FK_shares_file_entries_target_entry_id",
                    column: x => x.target_entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_shares_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "share_members",
            columns: table => new
            {
                share_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                permission = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_share_members", x => new { x.share_id, x.user_id });
                table.CheckConstraint("ck_share_members_permission", "\"permission\" IN ('VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER')");
                table.ForeignKey(
                    name: "FK_share_members_shares_share_id",
                    column: x => x.share_id,
                    principalTable: "shares",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_share_members_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_upload_sessions_actor_status",
            table: "upload_sessions",
            columns: new[] { "actor_user_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_upload_sessions_target_owner_user_id",
            table: "upload_sessions",
            column: "target_owner_user_id");

        migrationBuilder.CreateIndex(
            name: "ux_upload_sessions_actor_idempotency_key",
            table: "upload_sessions",
            columns: new[] { "actor_user_id", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_share_members_user_share",
            table: "share_members",
            columns: new[] { "user_id", "share_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shares_owner_updated_id",
            table: "shares",
            columns: new[] { "owner_user_id", "updated_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_shares_target_entry_id",
            table: "shares",
            column: "target_entry_id",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_upload_sessions_users_actor_user_id",
            table: "upload_sessions",
            column: "actor_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_upload_sessions_users_target_owner_user_id",
            table: "upload_sessions",
            column: "target_owner_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM shares) OR EXISTS (
                    SELECT 1
                    FROM upload_sessions
                    WHERE actor_user_id <> target_owner_user_id
                ) THEN
                    RAISE EXCEPTION 'Cannot roll back file sharing while shares or shared-target upload sessions exist.';
                END IF;
            END $$;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_upload_sessions_users_actor_user_id",
            table: "upload_sessions");

        migrationBuilder.DropForeignKey(
            name: "FK_upload_sessions_users_target_owner_user_id",
            table: "upload_sessions");

        migrationBuilder.DropTable(
            name: "share_members");

        migrationBuilder.DropTable(
            name: "shares");

        migrationBuilder.DropIndex(
            name: "ix_upload_sessions_actor_status",
            table: "upload_sessions");

        migrationBuilder.DropIndex(
            name: "IX_upload_sessions_target_owner_user_id",
            table: "upload_sessions");

        migrationBuilder.DropIndex(
            name: "ux_upload_sessions_actor_idempotency_key",
            table: "upload_sessions");

        migrationBuilder.DropColumn(
            name: "target_owner_user_id",
            table: "upload_sessions");

        migrationBuilder.RenameColumn(
            name: "actor_user_id",
            table: "upload_sessions",
            newName: "owner_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_upload_sessions_owner_status",
            table: "upload_sessions",
            columns: new[] { "owner_user_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ux_upload_sessions_owner_idempotency_key",
            table: "upload_sessions",
            columns: new[] { "owner_user_id", "idempotency_key" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_upload_sessions_users_owner_user_id",
            table: "upload_sessions",
            column: "owner_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}
