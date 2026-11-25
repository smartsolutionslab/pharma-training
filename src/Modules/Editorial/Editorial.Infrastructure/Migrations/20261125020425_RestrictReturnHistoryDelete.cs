using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125020425_RestrictReturnHistoryDelete : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_course_version_returns_course_versions_course_version_id",
            schema: "editorial",
            table: "course_version_returns");

        migrationBuilder.AddForeignKey(
            name: "FK_course_version_returns_course_versions_course_version_id",
            schema: "editorial",
            table: "course_version_returns",
            column: "course_version_id",
            principalSchema: "editorial",
            principalTable: "course_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_course_version_returns_course_versions_course_version_id",
            schema: "editorial",
            table: "course_version_returns");

        migrationBuilder.AddForeignKey(
            name: "FK_course_version_returns_course_versions_course_version_id",
            schema: "editorial",
            table: "course_version_returns",
            column: "course_version_id",
            principalSchema: "editorial",
            principalTable: "course_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }
}
