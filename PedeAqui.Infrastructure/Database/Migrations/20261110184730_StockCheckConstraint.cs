using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedeAqui.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class StockCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_products_stock_quantity_non_negative",
                table: "products",
                sql: "\"StockQuantity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_products_stock_quantity_non_negative",
                table: "products");
        }
    }
}
