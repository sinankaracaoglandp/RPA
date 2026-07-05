using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueClaimIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_QueueItems_QueueId_Status",
                table: "QueueItems",
                columns: new[] { "QueueId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueItems_QueueId_Status",
                table: "QueueItems");
        }
    }
}
