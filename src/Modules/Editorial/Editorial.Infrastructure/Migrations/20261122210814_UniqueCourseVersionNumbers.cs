using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122210814_UniqueCourseVersionNumbers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_course_versions_course_id",
            schema: "editorial",
            table: "course_versions");

        migrationBuilder.CreateIndex(
            name: "ix_course_versions_course_id_number",
            schema: "editorial",
            table: "course_versions",
            columns: new[] { "course_id", "number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_course_versions_course_id_number",
            schema: "editorial",
            table: "course_versions");

        migrationBuilder.CreateIndex(
            name: "IX_course_versions_course_id",
            schema: "editorial",
            table: "course_versions",
            column: "course_id");
    }
}
