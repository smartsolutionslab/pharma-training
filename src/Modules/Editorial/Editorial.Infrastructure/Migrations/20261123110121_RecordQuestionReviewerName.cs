using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123110121_RecordQuestionReviewerName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "reviewed_by_name",
            schema: "editorial",
            table: "course_version_questions",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "reviewed_by_name",
            schema: "editorial",
            table: "course_version_questions");
    }
}
