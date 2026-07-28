using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class PaymentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "payment_link_expires_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_link_hash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_payment_link_hash",
                table: "orders",
                column: "payment_link_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_payment_link_hash",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_link_expires_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_link_hash",
                table: "orders");
        }
    }
}
