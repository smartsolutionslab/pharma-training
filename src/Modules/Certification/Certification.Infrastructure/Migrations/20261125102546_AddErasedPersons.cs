using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125102546_AddErasedPersons : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "erased_persons",
            schema: "certification",
            columns: table => new
            {
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                erased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_erased_persons", x => x.person_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "erased_persons",
            schema: "certification");
    }
}
