using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124121313_RecordQuestionOrigin : Migration
{
    /// <summary>
    /// Records how each question came into its version, and whether an editor ever edited or approved it (#245, the
    /// owner's decisions of 2026-11-23 and 2026-11-24). The database keeps neither the drafts applied nor a link from a
    /// copy to the question it copies (a copy gets new ids, and so do the chapters), so the existing rows are classed from
    /// what they hold:
    /// <list type="bullet">
    /// <item>Copied: a question of a version numbered 2 or more whose text, type and provenance a question of the version
    /// before it (the published version it was drafted from) has too, in a chapter of the same title. A copy an editor
    /// changed since no longer matches and stays hand-written.</item>
    /// <item>AiDraft: the AI-drafted questions of a first version, which nothing copies into.</item>
    /// <item>HandWritten: every other question, as the issue says for those whose origin is not known. A draft never
    /// replaces a hand-written question, so one classed hand-written by doubt is kept, never lost.</item>
    /// </list>
    /// A question is approved once if it is approved now or is a copy (only a published version, every question of it
    /// approved, is copied); an approval a settings change took away before this migration left no trace. No question is
    /// recorded as edited: nothing in the table shows an edit. The defaults only fill the existing rows and are dropped.
    /// </summary>
    /// <param name="migrationBuilder">The builder the migration's operations are added to.</param>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "approval_history",
            schema: "editorial",
            table: "course_version_questions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "NeverApproved");

        migrationBuilder.AddColumn<string>(
            name: "edit_history",
            schema: "editorial",
            table: "course_version_questions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Untouched");

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
            SET origin = 'Copied'
            FROM editorial.course_versions AS version, editorial.course_version_chapters AS chapter
            WHERE version.id = question.course_version_id
              AND chapter.id = question.chapter_id
              AND version.number > 1
              AND EXISTS (
                  SELECT 1
                  FROM editorial.course_versions AS previous
                  JOIN editorial.course_version_questions AS source ON source.course_version_id = previous.id
                  JOIN editorial.course_version_chapters AS source_chapter ON source_chapter.id = source.chapter_id
                  WHERE previous.course_id = version.course_id
                    AND previous.number = version.number - 1
                    AND source.text = question.text
                    AND source.type = question.type
                    AND source.provenance = question.provenance
                    AND source_chapter.title = chapter.title);
            """);

        migrationBuilder.Sql(
            """
            UPDATE editorial.course_version_questions AS question
            SET origin = 'AiDraft'
            FROM editorial.course_versions AS version
            WHERE version.id = question.course_version_id
              AND version.number = 1
              AND question.provenance = 'AiDrafted';
            """);

        migrationBuilder.Sql(
            """
            UPDATE editorial.course_version_questions
            SET approval_history = 'ApprovedOnce'
            WHERE review = 'Approved' OR origin = 'Copied';
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE editorial.course_version_questions
                ALTER COLUMN origin DROP DEFAULT,
                ALTER COLUMN edit_history DROP DEFAULT,
                ALTER COLUMN approval_history DROP DEFAULT;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "approval_history",
            schema: "editorial",
            table: "course_version_questions");

        migrationBuilder.DropColumn(
            name: "edit_history",
            schema: "editorial",
            table: "course_version_questions");

        migrationBuilder.DropColumn(
            name: "origin",
            schema: "editorial",
            table: "course_version_questions");
    }
}
