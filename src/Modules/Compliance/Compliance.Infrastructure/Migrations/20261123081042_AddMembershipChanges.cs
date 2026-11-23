using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123081042_AddMembershipChanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "membership_changes",
            schema: "compliance",
            columns: table => new
            {
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                job_function = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_changes", x => new { x.membership_id, x.version });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "membership_changes",
            schema: "compliance");
    }
}
