using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123040159_AddTestDefinitionCopies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "test_definitions",
            schema: "assessment",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                passing_score_percent = table.Column<int>(type: "integer", nullable: false),
                max_attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_test_definitions", x => x.course_version_id);
            });

        migrationBuilder.CreateTable(
            name: "test_definition_questions",
            schema: "assessment",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                question_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                text = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_test_definition_questions", x => new { x.course_version_id, x.question_id });
                table.ForeignKey(
                    name: "fk_test_definition_questions_test_definitions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "assessment",
                    principalTable: "test_definitions",
                    principalColumn: "course_version_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "test_definition_options",
            schema: "assessment",
            columns: table => new
            {
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                question_id = table.Column<Guid>(type: "uuid", nullable: false),
                option_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                text = table.Column<string>(type: "text", nullable: false),
                is_correct = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_test_definition_options", x => new { x.course_version_id, x.question_id, x.option_id });
                table.ForeignKey(
                    name: "fk_test_definition_options_test_definition_questions",
                    columns: x => new { x.course_version_id, x.question_id },
                    principalSchema: "assessment",
                    principalTable: "test_definition_questions",
                    principalColumns: new[] { "course_version_id", "question_id" },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "test_definition_options",
            schema: "assessment");

        migrationBuilder.DropTable(
            name: "test_definition_questions",
            schema: "assessment");

        migrationBuilder.DropTable(
            name: "test_definitions",
            schema: "assessment");
    }
}
