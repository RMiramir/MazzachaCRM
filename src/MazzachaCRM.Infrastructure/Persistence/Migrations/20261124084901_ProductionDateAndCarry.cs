using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MazzachaCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionDateAndCarry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ProductionDate",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "CarriedUntil",
                table: "Batches",
                type: "TEXT",
                nullable: true);

            // День приготовления прежних заказов: день готовности, а если заказ ещё не готов — день выдачи.
            migrationBuilder.Sql("UPDATE Orders SET ProductionDate = COALESCE(ProducedDate, DueDate);");

            // Раньше остаток переходил на следующие дни сам. Теперь он перенесён до последнего дня,
            // в который из партии продавали или списывали; дальше — только по команде.
            migrationBuilder.Sql("""
                UPDATE Batches SET CarriedUntil = MAX(
                    COALESCE((SELECT MAX(Date) FROM BatchSales WHERE BatchSales.BatchId = Batches.Id), ''),
                    COALESCE((SELECT MAX(Date) FROM BatchWriteOffs WHERE BatchWriteOffs.BatchId = Batches.Id), ''));
                """);
            migrationBuilder.Sql("UPDATE Batches SET CarriedUntil = NULL WHERE CarriedUntil = '' OR CarriedUntil <= ProductionDate;");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ProductionDate",
                table: "Orders",
                column: "ProductionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_CarriedUntil",
                table: "Batches",
                column: "CarriedUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_ProductionDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Batches_CarriedUntil",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CarriedUntil",
                table: "Batches");
        }
    }
}
