using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123211825_AddMembershipCopies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "members",
            schema: "learning",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_members", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "membership_changes",
            schema: "learning",
            columns: table => new
            {
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_changes", x => new { x.membership_id, x.version });
            });

        migrationBuilder.CreateTable(
            name: "membership_endings",
            schema: "learning",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_endings", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_members_person_id",
            schema: "learning",
            table: "members",
            column: "person_id");

        migrationBuilder.CreateIndex(
            name: "ix_membership_changes_person_id",
            schema: "learning",
            table: "membership_changes",
            column: "person_id");

        migrationBuilder.CreateIndex(
            name: "ix_membership_endings_person_id",
            schema: "learning",
            table: "membership_endings",
            column: "person_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "members",
            schema: "learning");

        migrationBuilder.DropTable(
            name: "membership_changes",
            schema: "learning");

        migrationBuilder.DropTable(
            name: "membership_endings",
            schema: "learning");
    }
}
