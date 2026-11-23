using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122221525_RecordReturnAndRetirement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "retired_at",
            schema: "editorial",
            table: "course_versions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "retired_by",
            schema: "editorial",
            table: "course_versions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "returned_at",
            schema: "editorial",
            table: "course_versions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "returned_by",
            schema: "editorial",
            table: "course_versions",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "retired_at",
            schema: "editorial",
            table: "course_versions");

        migrationBuilder.DropColumn(
            name: "retired_by",
            schema: "editorial",
            table: "course_versions");

        migrationBuilder.DropColumn(
            name: "returned_at",
            schema: "editorial",
            table: "course_versions");

        migrationBuilder.DropColumn(
            name: "returned_by",
            schema: "editorial",
            table: "course_versions");
    }
}
