using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MazzachaCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusinessName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "Settings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "Settings");
        }
    }
}
