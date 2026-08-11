using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchIdToStudent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Students",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_BranchId",
                table: "Students",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Branches_BranchId",
                table: "Students",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 1. Ustawienie BranchId dla uczniów posiadających grupę
            migrationBuilder.Sql(
                "UPDATE \"Students\" SET \"BranchId\" = (SELECT \"BranchId\" FROM \"Groups\" WHERE \"Groups\".\"Id\" = \"Students\".\"GroupId\") WHERE \"GroupId\" IS NOT NULL");

            // 2. Ustawienie pierwszego napotkanego oddziału dla uczniów bez grupy (np. tych 30)
            migrationBuilder.Sql(
                "UPDATE \"Students\" SET \"BranchId\" = (SELECT \"Id\" FROM \"Branches\" LIMIT 1) WHERE \"GroupId\" IS NULL AND \"BranchId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Students_Branches_BranchId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_BranchId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Students");
        }
    }
}
