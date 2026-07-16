namespace RPA.Infrastructure.Persistence.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

public partial class AddEInvoiceProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EInvoiceProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ProjectId = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 256, nullable: false),
                Description = table.Column<string>(maxLength: 1024, nullable: true),
                DraftDefinitionJson = table.Column<string>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                CreatedBy = table.Column<string>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: true),
                UpdatedBy = table.Column<string>(nullable: true),
                IsDeleted = table.Column<bool>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EInvoiceProfiles", x => x.Id);
                table.ForeignKey("FK_EInvoiceProfiles_Projects_ProjectId", x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EInvoiceProfileVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ProfileId = table.Column<Guid>(nullable: false),
                Version = table.Column<int>(nullable: false),
                DefinitionJson = table.Column<string>(nullable: false),
                OutputSchemaJson = table.Column<string>(nullable: false),
                PublishedAt = table.Column<DateTime>(nullable: false),
                PublishedBy = table.Column<Guid>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                CreatedBy = table.Column<string>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: true),
                UpdatedBy = table.Column<string>(nullable: true),
                IsDeleted = table.Column<bool>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EInvoiceProfileVersions", x => x.Id);
                table.ForeignKey("FK_EInvoiceProfileVersions_EInvoiceProfiles_ProfileId", x => x.ProfileId, "EInvoiceProfiles", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_EInvoiceProfiles_ProjectId_Name", "EInvoiceProfiles", new[] { "ProjectId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_EInvoiceProfileVersions_ProfileId_Version", "EInvoiceProfileVersions", new[] { "ProfileId", "Version" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EInvoiceProfileVersions");
        migrationBuilder.DropTable("EInvoiceProfiles");
    }
}
