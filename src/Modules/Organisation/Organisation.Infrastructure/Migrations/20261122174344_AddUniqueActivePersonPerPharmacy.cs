using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122174344_AddUniqueActivePersonPerPharmacy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_memberships_pharmacy_id_person_id",
            schema: "organisation",
            table: "memberships",
            columns: new[] { "pharmacy_id", "person_id" },
            unique: true,
            filter: "status = 'Active'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_memberships_pharmacy_id_person_id",
            schema: "organisation",
            table: "memberships");
    }
}
