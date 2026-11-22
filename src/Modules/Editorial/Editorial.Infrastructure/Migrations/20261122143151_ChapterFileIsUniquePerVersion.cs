using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122143151_ChapterFileIsUniquePerVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_course_version_chapters_course_version_id_file",
            schema: "editorial",
            table: "course_version_chapters",
            columns: new[] { "course_version_id", "file" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_course_version_chapters_course_version_id_file",
            schema: "editorial",
            table: "course_version_chapters");
    }
}
