using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124061016_RecordQuestionOrigin : Migration
{
    /// <summary>
    /// Records how each question came into its version (#245). The database keeps no history of applied AI drafts, only
    /// each question's provenance, which a copy keeps: an AI-drafted question in a later version may have been drafted
    /// there or copied from the version before. So existing questions are hand-written, as the issue says for those whose
    /// draft is not known, except the AI-drafted ones of a first version, which nothing copies into: those came from a
    /// draft applied to that version. A hand-written question is never replaced by a draft, so a question classed
    /// hand-written by doubt is kept, never lost. The default only fills the existing rows and is dropped again.
    /// </summary>
    /// <param name="migrationBuilder">The builder the migration's operations are added to.</param>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "origin",
            schema: "editorial",
            table: "course_version_questions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "HandWritten");

        migrationBuilder.Sql(
            """
            UPDATE editorial.course_version_questions AS question
            SET origin = 'AiDraft'
            FROM editorial.course_versions AS version
            WHERE version.id = question.course_version_id
              AND version.number = 1
              AND question.provenance = 'AiDrafted';
            """);

        migrationBuilder.Sql("ALTER TABLE editorial.course_version_questions ALTER COLUMN origin DROP DEFAULT;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "origin",
            schema: "editorial",
            table: "course_version_questions");
    }
}
