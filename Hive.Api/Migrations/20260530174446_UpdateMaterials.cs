using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Materials_TaskId",
                table: "Materials",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Tasks_TaskId",
                table: "Materials",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Tasks_TaskId",
                table: "Materials");

            migrationBuilder.DropIndex(
                name: "IX_Materials_TaskId",
                table: "Materials");
        }
    }
}
