using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hive.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TaskId",
                table: "Materials",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "Materials");
        }
    }
}
