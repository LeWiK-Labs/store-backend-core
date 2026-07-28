using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeWiK.Store.App.Migrations
{
    /// <inheritdoc />
    public partial class OrderPaymentConcurrencyToken : Migration
    {
        // Deliberately empty. Mapping `xmin` as a rowversion changes the MODEL, not the schema:
        // xmin is a Postgres system column that already exists on every table (attnum -2), which
        // is why Npgsql omits it from CREATE TABLE. The scaffolded body called AddColumn, and
        // Postgres rejects that outright:
        //     ERROR: column name "xmin" conflicts with a system column name
        // The migration still has to exist so the model snapshot stays in sync — without it EF
        // reports a pending model change forever. Same pattern as inventories/preorders, which
        // got their xmin mapping inside the initial CreateTable.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
