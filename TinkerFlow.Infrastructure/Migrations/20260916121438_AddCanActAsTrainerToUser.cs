using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanActAsTrainerToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanActAsTrainer",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanActAsTrainer",
                table: "AspNetUsers");
        }
    }
}
