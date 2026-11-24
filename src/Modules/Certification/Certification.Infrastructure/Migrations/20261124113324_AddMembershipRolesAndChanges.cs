using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124113324_AddMembershipRolesAndChanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "role",
            schema: "certification",
            table: "memberships",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Employee");

        migrationBuilder.CreateTable(
            name: "membership_changes",
            schema: "certification",
            columns: table => new
            {
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_changes", x => new { x.membership_id, x.version });
            });

        migrationBuilder.CreateIndex(
            name: "ix_memberships_pharmacy_id",
            schema: "certification",
            table: "memberships",
            column: "pharmacy_id");

        migrationBuilder.CreateIndex(
            name: "ix_certificates_holder_id_issued_at",
            schema: "certification",
            table: "certificates",
            columns: new[] { "holder_id", "issued_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "membership_changes",
            schema: "certification");

        migrationBuilder.DropIndex(
            name: "ix_memberships_pharmacy_id",
            schema: "certification",
            table: "memberships");

        migrationBuilder.DropIndex(
            name: "ix_certificates_holder_id_issued_at",
            schema: "certification",
            table: "certificates");

        migrationBuilder.DropColumn(
            name: "role",
            schema: "certification",
            table: "memberships");
    }
}
