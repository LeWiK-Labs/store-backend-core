using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class ProductOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_options_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_option_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_option_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_option_values_product_options_product_option_id",
                        column: x => x.product_option_id,
                        principalTable: "product_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_option_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_option_value_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_option_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_variant_option_values_product_option_values_product_option_",
                        column: x => x.product_option_value_id,
                        principalTable: "product_option_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_variant_option_values_product_variants_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_option_values_product_option_id",
                table: "product_option_values",
                column: "product_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_options_product_id",
                table: "product_options",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_variant_option_values_product_option_value_id",
                table: "variant_option_values",
                column: "product_option_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_variant_option_values_product_variant_id_product_option_val",
                table: "variant_option_values",
                columns: new[] { "product_variant_id", "product_option_value_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "variant_option_values");

            migrationBuilder.DropTable(
                name: "product_option_values");

            migrationBuilder.DropTable(
                name: "product_options");
        }
    }
}
