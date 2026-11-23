using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123001536_AddCourseVersionCopies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_versions",
            schema: "learning",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                version_number = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_versions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "course_version_chapters",
            schema: "learning",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                video_duration_seconds = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_version_chapters", x => new { x.course_version_id, x.chapter_id });
                table.ForeignKey(
                    name: "fk_course_version_chapters_course_versions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "learning",
                    principalTable: "course_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_version_chapters",
            schema: "learning");

        migrationBuilder.DropTable(
            name: "course_versions",
            schema: "learning");
    }
}
