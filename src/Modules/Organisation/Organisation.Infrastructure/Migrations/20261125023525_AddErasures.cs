using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125023525_AddErasures : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "email",
            schema: "organisation",
            table: "memberships",
            type: "character varying(254)",
            maxLength: 254,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(254)",
            oldMaxLength: 254);

        migrationBuilder.CreateTable(
            name: "erasures",
            schema: "organisation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                confirmation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                confirmation_handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_erasures", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "erasure_steps",
            schema: "organisation",
            columns: table => new
            {
                module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                erasure_id = table.Column<Guid>(type: "uuid", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_erasure_steps", x => new { x.erasure_id, x.module });
                table.ForeignKey(
                    name: "FK_erasure_steps_erasures_erasure_id",
                    column: x => x.erasure_id,
                    principalSchema: "organisation",
                    principalTable: "erasures",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "erasure_steps",
            schema: "organisation");

        migrationBuilder.DropTable(
            name: "erasures",
            schema: "organisation");

        migrationBuilder.AlterColumn<string>(
            name: "email",
            schema: "organisation",
            table: "memberships",
            type: "character varying(254)",
            maxLength: 254,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(254)",
            oldMaxLength: 254,
            oldNullable: true);
    }
}
