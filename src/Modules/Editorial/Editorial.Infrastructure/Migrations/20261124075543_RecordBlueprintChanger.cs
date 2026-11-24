using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124075543_RecordBlueprintChanger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "blueprint_changed_by",
            schema: "editorial",
            table: "course_versions",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "blueprint_changed_by",
            schema: "editorial",
            table: "course_versions");
    }
}
