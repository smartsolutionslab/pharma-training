using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123141707_IndexCourseVersionChaptersByChapter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_course_version_chapters_chapter_id",
            schema: "learning",
            table: "course_version_chapters",
            column: "chapter_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_course_version_chapters_chapter_id",
            schema: "learning",
            table: "course_version_chapters");
    }
}
