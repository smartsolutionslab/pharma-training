using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123233130_AddAssignmentFollowsAssignment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "follows_assignment_id",
            schema: "compliance",
            table: "assignments",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_assignments_follows_assignment_id",
            schema: "compliance",
            table: "assignments",
            column: "follows_assignment_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_assignments_follows_assignment_id",
            schema: "compliance",
            table: "assignments");

        migrationBuilder.DropColumn(
            name: "follows_assignment_id",
            schema: "compliance",
            table: "assignments");
    }
}
