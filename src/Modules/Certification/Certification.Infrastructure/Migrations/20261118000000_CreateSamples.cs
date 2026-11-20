using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261118000000_CreateSamples : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "certification");

        migrationBuilder.CreateTable(
            name: "samples",
            schema: "certification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_samples", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "samples",
            schema: "certification");
    }
}
