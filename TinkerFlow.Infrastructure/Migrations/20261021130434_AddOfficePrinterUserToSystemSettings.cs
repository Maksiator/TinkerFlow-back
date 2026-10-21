using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficePrinterUserToSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OfficePrinterUserId",
                table: "SystemSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_OfficePrinterUserId",
                table: "SystemSettings",
                column: "OfficePrinterUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_AspNetUsers_OfficePrinterUserId",
                table: "SystemSettings",
                column: "OfficePrinterUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_AspNetUsers_OfficePrinterUserId",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_OfficePrinterUserId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "OfficePrinterUserId",
                table: "SystemSettings");
        }
    }
}
