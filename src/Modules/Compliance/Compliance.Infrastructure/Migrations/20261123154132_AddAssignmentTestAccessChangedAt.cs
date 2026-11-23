using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123154132_AddAssignmentTestAccessChangedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "test_access_changed_at",
            schema: "compliance",
            table: "assignments",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "test_access_changed_at",
            schema: "compliance",
            table: "assignments");
    }
}
