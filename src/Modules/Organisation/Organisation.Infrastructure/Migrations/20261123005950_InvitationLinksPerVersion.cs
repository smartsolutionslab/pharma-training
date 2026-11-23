using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123005950_InvitationLinksPerVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_memberships_token_hash",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.DropColumn(
            name: "token_hash",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.AddColumn<int>(
            name: "invitation_mailed_for",
            schema: "organisation",
            table: "memberships",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "invitation_version",
            schema: "organisation",
            table: "memberships",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.CreateTable(
            name: "invitation_links",
            schema: "organisation",
            columns: table => new
            {
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invitation_links", x => new { x.membership_id, x.token_hash });
                table.ForeignKey(
                    name: "FK_invitation_links_memberships_membership_id",
                    column: x => x.membership_id,
                    principalSchema: "organisation",
                    principalTable: "memberships",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_invitation_links_token_hash",
            schema: "organisation",
            table: "invitation_links",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invitation_links",
            schema: "organisation");

        migrationBuilder.DropColumn(
            name: "invitation_mailed_for",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.DropColumn(
            name: "invitation_version",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.AddColumn<string>(
            name: "token_hash",
            schema: "organisation",
            table: "memberships",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_memberships_token_hash",
            schema: "organisation",
            table: "memberships",
            column: "token_hash",
            unique: true);
    }
}
