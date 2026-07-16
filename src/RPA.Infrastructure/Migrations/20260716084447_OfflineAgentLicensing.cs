using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfflineAgentLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenseInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublicKey = table.Column<string>(type: "text", nullable: false),
                    PublicKeyFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CustomerReference = table.Column<string>(type: "text", nullable: true),
                    InstallationCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignedLicenseDocument = table.Column<string>(type: "text", nullable: true),
                    InstalledLicenseRevision = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseInstallations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MachineFingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CredentialHash = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentIdentities_LicenseInstallations_LicenseInstallationId",
                        column: x => x.LicenseInstallationId,
                        principalTable: "LicenseInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentActivations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivationCodeHash = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentActivations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentActivations_AgentIdentities_AgentIdentityId",
                        column: x => x.AgentIdentityId,
                        principalTable: "AgentIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivations_ActivationCodeHash",
                table: "AgentActivations",
                column: "ActivationCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivations_AgentIdentityId",
                table: "AgentActivations",
                column: "AgentIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentIdentities_LicenseInstallationId_MachineFingerprint",
                table: "AgentIdentities",
                columns: new[] { "LicenseInstallationId", "MachineFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseInstallations_InstallationId",
                table: "LicenseInstallations",
                column: "InstallationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentActivations");

            migrationBuilder.DropTable(
                name: "AgentIdentities");

            migrationBuilder.DropTable(
                name: "LicenseInstallations");
        }
    }
}
