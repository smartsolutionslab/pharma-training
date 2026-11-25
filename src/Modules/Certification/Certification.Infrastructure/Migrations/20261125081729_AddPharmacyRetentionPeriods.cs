using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125081729_AddPharmacyRetentionPeriods : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pharmacy_retention_periods",
            schema: "certification",
            columns: table => new
            {
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                years = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pharmacy_retention_periods", x => x.pharmacy_id);
                table.CheckConstraint("ck_pharmacy_retention_periods_years", "years BETWEEN 2 AND 30");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "pharmacy_retention_periods",
            schema: "certification");
    }
}
