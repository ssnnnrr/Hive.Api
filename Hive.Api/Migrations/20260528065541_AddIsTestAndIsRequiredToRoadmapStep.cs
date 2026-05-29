using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsTestAndIsRequiredToRoadmapStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "RoadmapSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "RoadmapSteps");
        }
    }
}
