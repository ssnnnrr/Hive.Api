using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeaching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StudentComment",
                table: "RoadmapSteps",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StudentComment",
                table: "RoadmapSteps");
        }
    }
}
