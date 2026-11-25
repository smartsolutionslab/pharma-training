using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125055822_AddErasedPersons : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "compliance",
            table: "membership_changes",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "compliance",
            table: "members",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.CreateTable(
            name: "erased_persons",
            schema: "compliance",
            columns: table => new
            {
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                erased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_erased_persons", x => x.person_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "erased_persons",
            schema: "compliance");

        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "compliance",
            table: "membership_changes",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "compliance",
            table: "members",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);
    }
}
