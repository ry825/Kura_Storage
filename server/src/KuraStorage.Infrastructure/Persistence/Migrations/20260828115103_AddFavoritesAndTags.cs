using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuraStorage.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFavoritesAndTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "favorite_entries",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                favorited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_favorite_entries", x => new { x.user_id, x.entry_id });
                table.ForeignKey(
                    name: "FK_favorite_entries_file_entries_entry_id",
                    column: x => x.entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_favorite_entries_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "tags",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                name_key = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tags", x => x.id);
                table.CheckConstraint("ck_tags_name", "char_length(name) BETWEEN 1 AND 50 AND name = btrim(name)");
                table.ForeignKey(
                    name: "FK_tags_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "entry_tags",
            columns: table => new
            {
                tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_entry_tags", x => new { x.tag_id, x.entry_id });
                table.ForeignKey(
                    name: "FK_entry_tags_file_entries_entry_id",
                    column: x => x.entry_id,
                    principalTable: "file_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_entry_tags_tags_tag_id",
                    column: x => x.tag_id,
                    principalTable: "tags",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_entry_tags_entry_id_tag_id",
            table: "entry_tags",
            columns: new[] { "entry_id", "tag_id" });

        migrationBuilder.CreateIndex(
            name: "ix_favorite_entries_entry_id",
            table: "favorite_entries",
            column: "entry_id");

        migrationBuilder.CreateIndex(
            name: "ix_favorite_entries_user_favorited_at_entry_id",
            table: "favorite_entries",
            columns: new[] { "user_id", "favorited_at", "entry_id" },
            descending: new[] { false, true, false });

        migrationBuilder.CreateIndex(
            name: "ix_tags_user_name_key_id",
            table: "tags",
            columns: new[] { "user_id", "name_key", "id" });

        migrationBuilder.CreateIndex(
            name: "ux_tags_user_name_key",
            table: "tags",
            columns: new[] { "user_id", "name_key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "entry_tags");

        migrationBuilder.DropTable(
            name: "favorite_entries");

        migrationBuilder.DropTable(
            name: "tags");
    }
}
