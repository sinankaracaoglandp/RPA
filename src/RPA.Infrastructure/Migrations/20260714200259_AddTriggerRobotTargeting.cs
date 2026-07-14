using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggerRobotTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Triggers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TargetRobotTags",
                table: "Triggers",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Triggers");

            migrationBuilder.DropColumn(
                name: "TargetRobotTags",
                table: "Triggers");
        }
    }
}
