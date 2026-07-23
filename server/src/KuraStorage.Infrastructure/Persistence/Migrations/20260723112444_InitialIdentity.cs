using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_os_user = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                target_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_logs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "authentication_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: true),
                result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                remote_address = table.Column<IPAddress>(type: "inet", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_authentication_attempts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username_normalized = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                password_hash = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                failed_login_count = table.Column<int>(type: "integer", nullable: false),
                failed_login_window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lock_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "devices",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_devices", x => x.id);
                table.ForeignKey(
                    name: "FK_devices_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                family_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                replaced_by_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_sessions", x => x.id);
                table.ForeignKey(
                    name: "FK_refresh_sessions_devices_device_id",
                    column: x => x.device_id,
                    principalTable: "devices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_refresh_sessions_refresh_sessions_replaced_by_session_id",
                    column: x => x.replaced_by_session_id,
                    principalTable: "refresh_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_refresh_sessions_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_actor_user_id_created_at",
            table: "audit_logs",
            columns: new[] { "actor_user_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_created_at",
            table: "audit_logs",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "IX_authentication_attempts_username_hash_created_at",
            table: "authentication_attempts",
            columns: new[] { "username_hash", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_devices_user_id_status",
            table: "devices",
            columns: new[] { "user_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_refresh_sessions_device_id",
            table: "refresh_sessions",
            column: "device_id",
            unique: true,
            filter: "revoked_at IS NULL AND used_at IS NULL AND replaced_by_session_id IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_sessions_family_id",
            table: "refresh_sessions",
            column: "family_id");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_sessions_replaced_by_session_id",
            table: "refresh_sessions",
            column: "replaced_by_session_id");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_sessions_token_hash",
            table: "refresh_sessions",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_refresh_sessions_user_id",
            table: "refresh_sessions",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_users_username_normalized",
            table: "users",
            column: "username_normalized",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_logs");

        migrationBuilder.DropTable(
            name: "authentication_attempts");

        migrationBuilder.DropTable(
            name: "refresh_sessions");

        migrationBuilder.DropTable(
            name: "devices");

        migrationBuilder.DropTable(
            name: "users");
    }
}
