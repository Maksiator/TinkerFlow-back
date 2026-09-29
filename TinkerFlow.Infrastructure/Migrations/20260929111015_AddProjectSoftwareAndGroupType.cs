using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSoftwareAndGroupType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdvanced",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Software",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAdvanced",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Software",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Groups");
        }
    }
}
