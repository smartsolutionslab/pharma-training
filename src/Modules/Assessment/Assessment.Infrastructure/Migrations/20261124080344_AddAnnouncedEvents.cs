using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124080344_AddAnnouncedEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "announced_events",
            schema: "assessment",
            columns: table => new
            {
                stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                stream_version = table.Column<long>(type: "bigint", nullable: false),
                announced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_announced_events", x => new { x.stream_id, x.stream_version });
            });

        // #277: every test pass stored before markers existed counts as announced, so the sweep's first pass announces
        // none of them again. On a fresh database Marten's tables do not exist yet, and there is nothing to mark.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF to_regclass('assessment.mt_events') IS NOT NULL THEN
                INSERT INTO assessment.announced_events (stream_id, stream_version, announced_at)
                SELECT stream_id, version, timestamp FROM assessment.mt_events WHERE type = 'examination_passed'
                ON CONFLICT DO NOTHING;
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "announced_events",
            schema: "assessment");
    }
}
