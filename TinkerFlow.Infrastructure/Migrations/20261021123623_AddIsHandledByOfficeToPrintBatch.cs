using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsHandledByOfficeToPrintBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHandledByOffice",
                table: "PrintBatches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHandledByOffice",
                table: "PrintBatches");
        }
    }
}
