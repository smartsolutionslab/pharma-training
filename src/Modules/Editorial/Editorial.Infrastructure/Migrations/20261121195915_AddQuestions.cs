using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121195915_AddQuestions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_version_questions",
            schema: "editorial",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                provenance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                review = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_course_version_questions", x => x.id);
                table.ForeignKey(
                    name: "FK_course_version_questions_course_version_chapters_chapter_id",
                    column: x => x.chapter_id,
                    principalSchema: "editorial",
                    principalTable: "course_version_chapters",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_course_version_questions_course_versions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "editorial",
                    principalTable: "course_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "course_version_answer_options",
            schema: "editorial",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                text = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                correctness = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                question_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_course_version_answer_options", x => x.id);
                table.ForeignKey(
                    name: "FK_course_version_answer_options_course_version_questions_ques~",
                    column: x => x.question_id,
                    principalSchema: "editorial",
                    principalTable: "course_version_questions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_course_version_answer_options_question_id",
            schema: "editorial",
            table: "course_version_answer_options",
            column: "question_id");

        migrationBuilder.CreateIndex(
            name: "IX_course_version_questions_chapter_id",
            schema: "editorial",
            table: "course_version_questions",
            column: "chapter_id");

        migrationBuilder.CreateIndex(
            name: "IX_course_version_questions_course_version_id",
            schema: "editorial",
            table: "course_version_questions",
            column: "course_version_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_version_answer_options",
            schema: "editorial");

        migrationBuilder.DropTable(
            name: "course_version_questions",
            schema: "editorial");
    }
}
