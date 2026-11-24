using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124012644_KeepCourseVersionNumbers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "version_number",
            schema: "certification",
            table: "course_versions",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "course_version_number",
            schema: "certification",
            table: "certificates",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "version_number",
            schema: "certification",
            table: "course_versions");

        migrationBuilder.DropColumn(
            name: "course_version_number",
            schema: "certification",
            table: "certificates");
    }
}
