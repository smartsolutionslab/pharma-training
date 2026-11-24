using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124220757_AddAnnouncedFailuresAndExhaustions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // #307: every attempt submitted and every lock stored before their markers existed counts as announced, so the
        // first pass of the sweeps of failures and locks announces none of them again: a lock announced twice would count
        // as a second lock in Learning (docs/adr/0025). A passed attempt is marked too: its announcement is its pass's.
        // On a fresh database Marten's tables do not exist yet, and there is nothing to mark.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF to_regclass('assessment.mt_events') IS NOT NULL THEN
                INSERT INTO assessment.announced_events (stream_id, stream_version, announced_at)
                SELECT stream_id, version, timestamp FROM assessment.mt_events WHERE type IN ('attempt_submitted', 'examination_exhausted')
                ON CONFLICT DO NOTHING;
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing: the backfilled markers cannot be told from the markers of announcements that left since, and removing
        // those would make the sweeps announce them again.
    }
}
