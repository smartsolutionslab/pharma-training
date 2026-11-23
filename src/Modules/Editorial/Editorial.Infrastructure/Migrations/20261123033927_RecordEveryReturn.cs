using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123033927_RecordEveryReturn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "course_version_returns",
            schema: "editorial",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                returned_by = table.Column<Guid>(type: "uuid", nullable: false),
                returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_course_version_returns", x => x.id);
                table.ForeignKey(
                    name: "FK_course_version_returns_course_versions_course_version_id",
                    column: x => x.course_version_id,
                    principalSchema: "editorial",
                    principalTable: "course_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_course_version_returns_course_version_id_returned_at",
            schema: "editorial",
            table: "course_version_returns",
            columns: new[] { "course_version_id", "returned_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "course_version_returns",
            schema: "editorial");
    }
}
