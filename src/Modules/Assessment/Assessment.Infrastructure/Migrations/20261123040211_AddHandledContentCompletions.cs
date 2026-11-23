using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123040211_AddHandledContentCompletions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "handled_content_completions",
            schema: "assessment",
            columns: table => new
            {
                event_identifier = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_handled_content_completions", x => x.event_identifier);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "handled_content_completions",
            schema: "assessment");
    }
}
