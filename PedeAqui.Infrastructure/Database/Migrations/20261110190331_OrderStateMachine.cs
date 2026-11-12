using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedeAqui.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class OrderStateMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentStartedAt",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentStartedAt",
                table: "orders");
        }
    }
}
