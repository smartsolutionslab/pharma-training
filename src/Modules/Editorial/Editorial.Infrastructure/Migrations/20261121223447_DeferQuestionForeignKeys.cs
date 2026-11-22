using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <summary>
/// Defers the keys from questions to their chapter and from answer options to their question to the commit.
/// </summary>
/// <remarks>
/// EF Core sorts a save's commands by table, so the INSERTs and UPDATEs of questions and answer options run before
/// the course version's xmin UPDATE (RowVersionBump). A save that lost a race to a removal then failed on one of
/// these keys (23503) before the xmin check could report the lost race, and answered 500. Deferred, the keys are
/// checked at commit, after that UPDATE has reported the lost race (409). PostgreSQL never defers RESTRICT, so the
/// chapter key becomes NO ACTION; the Domain removes a chapter's questions itself. The model keeps Restrict, which
/// EF Core handles like NoAction on the client, so EF Core sees no change here. EF Core does not model
/// deferrability either: a later migration that drops and recreates either key must make it DEFERRABLE INITIALLY
/// DEFERRED again, and the chapter key NO ACTION.
/// </remarks>
public partial class _20261121223447_DeferQuestionForeignKeys : Migration
{
    private const string ChapterForeignKey = "FK_course_version_questions_course_version_chapters_chapter_id";

    // The name EF Core gave the key, truncated to PostgreSQL's 63 characters (see 20261121195915_AddQuestions).
    private const string QuestionForeignKey = "FK_course_version_answer_options_course_version_questions_ques~";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            ALTER TABLE editorial.course_version_questions
                DROP CONSTRAINT "{ChapterForeignKey}",
                ADD CONSTRAINT "{ChapterForeignKey}" FOREIGN KEY (chapter_id)
                    REFERENCES editorial.course_version_chapters (id) ON DELETE NO ACTION
                    DEFERRABLE INITIALLY DEFERRED;
            ALTER TABLE editorial.course_version_answer_options
                ALTER CONSTRAINT "{QuestionForeignKey}" DEFERRABLE INITIALLY DEFERRED;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            ALTER TABLE editorial.course_version_answer_options
                ALTER CONSTRAINT "{QuestionForeignKey}" NOT DEFERRABLE INITIALLY IMMEDIATE;
            ALTER TABLE editorial.course_version_questions
                DROP CONSTRAINT "{ChapterForeignKey}",
                ADD CONSTRAINT "{ChapterForeignKey}" FOREIGN KEY (chapter_id)
                    REFERENCES editorial.course_version_chapters (id) ON DELETE RESTRICT;
            """);
    }
}
