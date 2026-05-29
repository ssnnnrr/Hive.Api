using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddComentss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TaskCompletionId",
                table: "TaskCompletions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskComments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TaskCompletionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskComments_TaskCompletions_TaskCompletionId",
                        column: x => x.TaskCompletionId,
                        principalTable: "TaskCompletions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskComments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskComments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions",
                column: "TaskCompletionId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_CreatorId",
                table: "Materials",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskCompletionId",
                table: "TaskComments",
                column: "TaskCompletionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskId",
                table: "TaskComments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_UserId",
                table: "TaskComments",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials",
                column: "CreatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskCompletions_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions",
                column: "TaskCompletionId",
                principalTable: "TaskCompletions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Users_CreatorId",
                table: "Materials");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskCompletions_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions");

            migrationBuilder.DropTable(
                name: "TaskComments");

            migrationBuilder.DropIndex(
                name: "IX_TaskCompletions_TaskCompletionId",
                table: "TaskCompletions");

            migrationBuilder.DropIndex(
                name: "IX_Materials_CreatorId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "TaskCompletionId",
                table: "TaskCompletions");
        }
    }
}
