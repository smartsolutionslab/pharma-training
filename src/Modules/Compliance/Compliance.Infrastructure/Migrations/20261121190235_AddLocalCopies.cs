using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121190235_AddLocalCopies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_version_retirements",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_version_retirements", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "course_versions",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                version_number = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                validity_months = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_versions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "members",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                job_function = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_members", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "membership_endings",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_endings", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_course_version_retirements_course_id",
            schema: "compliance",
            table: "course_version_retirements",
            column: "course_id");

        migrationBuilder.CreateIndex(
            name: "ix_course_versions_course_id_version_number",
            schema: "compliance",
            table: "course_versions",
            columns: new[] { "course_id", "version_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_members_person_id",
            schema: "compliance",
            table: "members",
            column: "person_id");

        migrationBuilder.CreateIndex(
            name: "ix_members_pharmacy_id",
            schema: "compliance",
            table: "members",
            column: "pharmacy_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_version_retirements",
            schema: "compliance");

        migrationBuilder.DropTable(
            name: "course_versions",
            schema: "compliance");

        migrationBuilder.DropTable(
            name: "members",
            schema: "compliance");

        migrationBuilder.DropTable(
            name: "membership_endings",
            schema: "compliance");
    }
}
