using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124064005_AddChapterPdfText : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "pdf_text",
            schema: "editorial",
            table: "course_version_chapters",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "pdf_text_extraction",
            schema: "editorial",
            table: "course_version_chapters",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "pdf_text",
            schema: "editorial",
            table: "course_version_chapters");

        migrationBuilder.DropColumn(
            name: "pdf_text_extraction",
            schema: "editorial",
            table: "course_version_chapters");
    }
}
