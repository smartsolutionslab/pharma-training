using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121142518_AddPharmacies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pharmacies",
            schema: "organisation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pharmacies", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "memberships",
            schema: "organisation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                token = table.Column<string>(type: "text", nullable: true),
                invitation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                person_id = table.Column<Guid>(type: "uuid", nullable: true),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                job_function = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memberships", x => x.id);
                table.ForeignKey(
                    name: "FK_memberships_pharmacies_pharmacy_id",
                    column: x => x.pharmacy_id,
                    principalSchema: "organisation",
                    principalTable: "pharmacies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_memberships_person_id",
            schema: "organisation",
            table: "memberships",
            column: "person_id");

        migrationBuilder.CreateIndex(
            name: "IX_memberships_pharmacy_id",
            schema: "organisation",
            table: "memberships",
            column: "pharmacy_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "memberships",
            schema: "organisation");

        migrationBuilder.DropTable(
            name: "pharmacies",
            schema: "organisation");
    }
}
