using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123090843_CreateAssignments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "assignments",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                learner_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                recurrence_months = table.Column<int>(type: "integer", nullable: true),
                assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                test_access = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                certificate_id = table.Column<Guid>(type: "uuid", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                history = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_assignments", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_assignments_learner_id",
            schema: "compliance",
            table: "assignments",
            column: "learner_id");

        migrationBuilder.CreateIndex(
            name: "ix_assignments_pharmacy_id",
            schema: "compliance",
            table: "assignments",
            column: "pharmacy_id");

        migrationBuilder.CreateIndex(
            name: "ux_assignments_active_learner_pharmacy_course",
            schema: "compliance",
            table: "assignments",
            columns: new[] { "learner_id", "pharmacy_id", "course_id" },
            unique: true,
            filter: "status IN ('Open', 'InProgress')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "assignments",
            schema: "compliance");
    }
}
