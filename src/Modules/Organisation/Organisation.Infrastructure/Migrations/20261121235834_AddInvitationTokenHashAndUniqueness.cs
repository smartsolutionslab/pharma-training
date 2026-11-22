using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121235834_AddInvitationTokenHashAndUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "token",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.AddColumn<string>(
            name: "token_hash",
            schema: "organisation",
            table: "memberships",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "token_hash",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.AddColumn<string>(
            name: "token",
            schema: "organisation",
            table: "memberships",
            type: "text",
            nullable: true);
    }
}
