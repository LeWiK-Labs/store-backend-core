using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class Refunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                table: "orders",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                    // NO declarar xmin: Postgres ya se lo da a toda tabla como columna de
                    // sistema, y pedirlo en el CREATE TABLE lo rechaza de plano
                    // ("column name 'xmin' conflicts with a system column name").
                    // El modelo sí lo conoce (mapeado como rowversion en RefundConfiguration)
                    // y por eso el snapshot lo lista; acá solo hay que no crearlo.
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_order_id",
                table: "refunds",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id",
                table: "refunds",
                column: "payment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                table: "orders");
        }
    }
}
