using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStepCommentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoadmapStepComment_RoadmapSteps_RoadmapStepId",
                table: "RoadmapStepComment");

            migrationBuilder.DropForeignKey(
                name: "FK_RoadmapStepComment_Users_UserId",
                table: "RoadmapStepComment");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RoadmapStepComment",
                table: "RoadmapStepComment");

            migrationBuilder.RenameTable(
                name: "RoadmapStepComment",
                newName: "StepComments");

            migrationBuilder.RenameIndex(
                name: "IX_RoadmapStepComment_UserId",
                table: "StepComments",
                newName: "IX_StepComments_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_RoadmapStepComment_RoadmapStepId",
                table: "StepComments",
                newName: "IX_StepComments_RoadmapStepId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StepComments",
                table: "StepComments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StepComments_RoadmapSteps_RoadmapStepId",
                table: "StepComments",
                column: "RoadmapStepId",
                principalTable: "RoadmapSteps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StepComments_Users_UserId",
                table: "StepComments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StepComments_RoadmapSteps_RoadmapStepId",
                table: "StepComments");

            migrationBuilder.DropForeignKey(
                name: "FK_StepComments_Users_UserId",
                table: "StepComments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StepComments",
                table: "StepComments");

            migrationBuilder.RenameTable(
                name: "StepComments",
                newName: "RoadmapStepComment");

            migrationBuilder.RenameIndex(
                name: "IX_StepComments_UserId",
                table: "RoadmapStepComment",
                newName: "IX_RoadmapStepComment_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_StepComments_RoadmapStepId",
                table: "RoadmapStepComment",
                newName: "IX_RoadmapStepComment_RoadmapStepId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoadmapStepComment",
                table: "RoadmapStepComment",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RoadmapStepComment_RoadmapSteps_RoadmapStepId",
                table: "RoadmapStepComment",
                column: "RoadmapStepId",
                principalTable: "RoadmapSteps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoadmapStepComment_Users_UserId",
                table: "RoadmapStepComment",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
