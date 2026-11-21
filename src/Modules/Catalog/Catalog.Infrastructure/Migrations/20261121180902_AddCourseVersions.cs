using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121180902_AddCourseVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_version_retirements",
            schema: "catalog",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_version_retirements", x => x.course_version_id);
            });

        migrationBuilder.CreateTable(
            name: "course_versions",
            schema: "catalog",
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
            name: "course_chapters",
            schema: "catalog",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                video_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                text = table.Column<string>(type: "text", nullable: true),
                file = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_chapters", x => new { x.course_version_id, x.chapter_id });
                table.ForeignKey(
                    name: "fk_course_chapters_course_versions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "catalog",
                    principalTable: "course_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_course_versions_course_id_version_number",
            schema: "catalog",
            table: "course_versions",
            columns: new[] { "course_id", "version_number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_chapters",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "course_version_retirements",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "course_versions",
            schema: "catalog");
    }
}
