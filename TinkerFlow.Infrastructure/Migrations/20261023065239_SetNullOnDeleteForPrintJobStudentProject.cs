using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinkerFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetNullOnDeleteForPrintJobStudentProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_StudentProject_StudentProjectId",
                table: "PrintJobs");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_StudentProject_StudentProjectId",
                table: "PrintJobs",
                column: "StudentProjectId",
                principalTable: "StudentProject",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_StudentProject_StudentProjectId",
                table: "PrintJobs");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_StudentProject_StudentProjectId",
                table: "PrintJobs",
                column: "StudentProjectId",
                principalTable: "StudentProject",
                principalColumn: "Id");
        }
    }
}
