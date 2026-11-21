using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121152240_AddCourseVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_versions",
            schema: "editorial",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                published_by = table.Column<Guid>(type: "uuid", nullable: true),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                blueprint_passing_score = table.Column<int>(type: "integer", nullable: false),
                blueprint_questions = table.Column<int>(type: "integer", nullable: false),
                blueprint_validity_months = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_course_versions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "course_version_chapters",
            schema: "editorial",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                text = table.Column<string>(type: "text", nullable: true),
                file = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                duration_seconds = table.Column<int>(type: "integer", nullable: true),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_course_version_chapters", x => x.id);
                table.ForeignKey(
                    name: "FK_course_version_chapters_course_versions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "editorial",
                    principalTable: "course_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_course_version_chapters_course_version_id",
            schema: "editorial",
            table: "course_version_chapters",
            column: "course_version_id");

        migrationBuilder.CreateIndex(
            name: "IX_course_versions_course_id",
            schema: "editorial",
            table: "course_versions",
            column: "course_id");

        migrationBuilder.CreateIndex(
            name: "IX_course_versions_status",
            schema: "editorial",
            table: "course_versions",
            column: "status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_version_chapters",
            schema: "editorial");

        migrationBuilder.DropTable(
            name: "course_versions",
            schema: "editorial");
    }
}
