using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRobotAgentOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentIdentityId",
                table: "Robots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Robots_AgentIdentityId",
                table: "Robots",
                column: "AgentIdentityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Robots_AgentIdentityId",
                table: "Robots");

            migrationBuilder.DropColumn(
                name: "AgentIdentityId",
                table: "Robots");
        }
    }
}
