using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class CleanDatabaseStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskComments_TaskCompletions_TaskCompletionId",
                table: "TaskComments");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskCompletions_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions");

            migrationBuilder.DropIndex(
                name: "IX_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions");

            migrationBuilder.DropIndex(
                name: "IX_TaskComments_TaskCompletionId",
                table: "TaskComments");

            migrationBuilder.DropColumn(
                name: "TaskCompletionId",
                table: "TaskCompletions");

            migrationBuilder.DropColumn(
                name: "TaskCompletionId",
                table: "TaskComments");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials",
                column: "CreatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials");

            migrationBuilder.AddColumn<long>(
                name: "TaskCompletionId",
                table: "TaskCompletions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaskCompletionId",
                table: "TaskComments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions",
                column: "TaskCompletionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskCompletionId",
                table: "TaskComments",
                column: "TaskCompletionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials",
                column: "CreatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskComments_TaskCompletions_TaskCompletionId",
                table: "TaskComments",
                column: "TaskCompletionId",
                principalTable: "TaskCompletions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskCompletions_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions",
                column: "TaskCompletionId",
                principalTable: "TaskCompletions",
                principalColumn: "Id");
        }
    }
}
