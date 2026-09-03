using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedPrinterToGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedPrinterId",
                table: "Groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_AssignedPrinterId",
                table: "Groups",
                column: "AssignedPrinterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_AspNetUsers_AssignedPrinterId",
                table: "Groups",
                column: "AssignedPrinterId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_AspNetUsers_AssignedPrinterId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_AssignedPrinterId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "AssignedPrinterId",
                table: "Groups");
        }
    }
}
