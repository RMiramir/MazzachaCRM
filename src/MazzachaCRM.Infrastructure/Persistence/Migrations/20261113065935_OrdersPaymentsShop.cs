using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MazzachaCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersPaymentsShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Предоплата становится первым платежом. Один платёж на заказ, поэтому
            // идентификатор платежа можно взять у заказа: он уникален.
            migrationBuilder.Sql(
                """
                INSERT INTO "OrderPayments" ("Id", "OrderId", "Date", "Amount", "Note")
                SELECT "Id", "Id", substr("CreatedAt", 1, 10), "Prepayment", 'Предоплата'
                FROM "Orders"
                WHERE CAST("Prepayment" AS REAL) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "Prepayment",
                table: "Orders");

            // Дата выдачи остаётся той же, меняется только название.
            migrationBuilder.RenameColumn(
                name: "CompletedDate",
                table: "Orders",
                newName: "DeliveredDate");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReportedDate",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyProfitGoal",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostMode",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualUnitCost",
                table: "Products",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "OrderItems",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsignment",
                table: "Customers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderItemId",
                table: "Batches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_Date",
                table: "OrderPayments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "MonthlyProfitGoal",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CostMode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ManualUnitCost",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReportedDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsConsignment",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OrderItemId",
                table: "Batches");

            migrationBuilder.RenameColumn(
                name: "DeliveredDate",
                table: "Orders",
                newName: "CompletedDate");

            migrationBuilder.AddColumn<decimal>(
                name: "Prepayment",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
