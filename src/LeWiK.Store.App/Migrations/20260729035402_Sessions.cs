using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class Sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_platform_operator_id",
                table: "platform_sessions",
                column: "platform_operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_token_hash",
                table: "platform_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_staff_user_id",
                table: "staff_sessions",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_token_hash",
                table: "staff_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_sessions");

            migrationBuilder.DropTable(
                name: "staff_sessions");
        }
    }
}
