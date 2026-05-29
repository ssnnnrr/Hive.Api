using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateChatAndRoadmapForSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "RoadmapSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TestData",
                table: "RoadmapSteps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TestScore",
                table: "RoadmapSteps",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ChatMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RoadmapStepComment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoadmapStepId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoadmapStepComment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoadmapStepComment_RoadmapSteps_RoadmapStepId",
                        column: x => x.RoadmapStepId,
                        principalTable: "RoadmapSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoadmapStepComment_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapStepComment_RoadmapStepId",
                table: "RoadmapStepComment",
                column: "RoadmapStepId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapStepComment_UserId",
                table: "RoadmapStepComment",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoadmapStepComment");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "RoadmapSteps");

            migrationBuilder.DropColumn(
                name: "TestData",
                table: "RoadmapSteps");

            migrationBuilder.DropColumn(
                name: "TestScore",
                table: "RoadmapSteps");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatMessages");
        }
    }
}
