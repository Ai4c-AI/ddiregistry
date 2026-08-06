using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ddi.Registry.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripleRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConceptRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Irdi = table.Column<string>(type: "text", nullable: false),
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    Definition = table.Column<string>(type: "text", nullable: true),
                    DomainOntology = table.Column<string>(type: "text", nullable: true),
                    MapsToClass = table.Column<string>(type: "text", nullable: true),
                    ApprovalState = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptRegistrations", x => x.Id);
                    table.UniqueConstraint("AK_ConceptRegistrations_Irdi", x => x.Irdi);
                });

            migrationBuilder.CreateTable(
                name: "ConceptRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceConceptIrdi = table.Column<string>(type: "text", nullable: false),
                    TargetConceptIrdi = table.Column<string>(type: "text", nullable: true),
                    TargetExternalIrdi = table.Column<string>(type: "text", nullable: true),
                    IsCrossAgency = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptRelations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepresentationRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Irdi = table.Column<string>(type: "text", nullable: false),
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    JsonSchema = table.Column<string>(type: "text", nullable: true),
                    ShaclTemplateIrdi = table.Column<string>(type: "text", nullable: true),
                    ApprovalState = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepresentationRegistrations", x => x.Id);
                    table.UniqueConstraint("AK_RepresentationRegistrations_Irdi", x => x.Irdi);
                });

            migrationBuilder.CreateTable(
                name: "VariableRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Irdi = table.Column<string>(type: "text", nullable: false),
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ConceptIrdi = table.Column<string>(type: "text", nullable: false),
                    RepresentationIrdi = table.Column<string>(type: "text", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: true),
                    CollectionMethod = table.Column<string>(type: "text", nullable: true),
                    Universe = table.Column<string>(type: "text", nullable: true),
                    QualityGate = table.Column<string>(type: "text", nullable: true),
                    ApprovalState = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariableRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariableRegistrations_ConceptRegistrations_ConceptIrdi",
                        column: x => x.ConceptIrdi,
                        principalTable: "ConceptRegistrations",
                        principalColumn: "Irdi",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariableRegistrations_RepresentationRegistrations_Represent~",
                        column: x => x.RepresentationIrdi,
                        principalTable: "RepresentationRegistrations",
                        principalColumn: "Irdi",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRegistrations_AgencyId",
                table: "ConceptRegistrations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRegistrations_AgencyId_Name_Version",
                table: "ConceptRegistrations",
                columns: new[] { "AgencyId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRegistrations_ApprovalState",
                table: "ConceptRegistrations",
                column: "ApprovalState");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRegistrations_CreatedAt",
                table: "ConceptRegistrations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRegistrations_Irdi",
                table: "ConceptRegistrations",
                column: "Irdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRegistrations_AgencyId",
                table: "RepresentationRegistrations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRegistrations_AgencyId_Name_Version",
                table: "RepresentationRegistrations",
                columns: new[] { "AgencyId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRegistrations_ApprovalState",
                table: "RepresentationRegistrations",
                column: "ApprovalState");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRegistrations_CreatedAt",
                table: "RepresentationRegistrations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRegistrations_Irdi",
                table: "RepresentationRegistrations",
                column: "Irdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_AgencyId",
                table: "VariableRegistrations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_AgencyId_Name_Version",
                table: "VariableRegistrations",
                columns: new[] { "AgencyId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_ApprovalState",
                table: "VariableRegistrations",
                column: "ApprovalState");

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_ConceptIrdi",
                table: "VariableRegistrations",
                column: "ConceptIrdi");

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_CreatedAt",
                table: "VariableRegistrations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_Irdi",
                table: "VariableRegistrations",
                column: "Irdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariableRegistrations_RepresentationIrdi",
                table: "VariableRegistrations",
                column: "RepresentationIrdi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConceptRelations");

            migrationBuilder.DropTable(
                name: "VariableRegistrations");

            migrationBuilder.DropTable(
                name: "ConceptRegistrations");

            migrationBuilder.DropTable(
                name: "RepresentationRegistrations");
        }
    }
}
