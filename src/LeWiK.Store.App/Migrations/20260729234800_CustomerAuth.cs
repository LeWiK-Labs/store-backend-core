using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Customer.Email is now normalised on construction, but rows written before that
            // keep whatever case they arrived with — and the (tenant_id, email) index is
            // case-sensitive, so a lookup for the lowercased form would miss them and create a
            // second customer for the same person. This is the one chance to fix them.
            //
            // If two rows in a store already differ only by case, this fails and the migration
            // rolls back. That is correct: merging two customers means deciding what happens to
            // two order histories, which is a human's call and not a migration's.
            migrationBuilder.Sql("UPDATE customers SET email = lower(email) WHERE email <> lower(email);");

            migrationBuilder.CreateTable(
                name: "customer_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_sessions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_sessions_customer_id",
                table: "customer_sessions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_sessions_token_hash",
                table: "customer_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_sessions");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "customers");
        }
    }
}
