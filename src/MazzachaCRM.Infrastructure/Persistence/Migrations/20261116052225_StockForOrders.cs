using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MazzachaCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockForOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceBeforeOrder",
                table: "Batches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReservedFromStock",
                table: "Batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SplitFromBatchId",
                table: "Batches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceBeforeOrder",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ReservedFromStock",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "SplitFromBatchId",
                table: "Batches");
        }
    }
}
