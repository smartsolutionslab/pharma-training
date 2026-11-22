using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122000311_AddInvitationUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_memberships_pharmacy_id_email",
            schema: "organisation",
            table: "memberships",
            columns: new[] { "pharmacy_id", "email" },
            unique: true,
            filter: "status IN ('Invited', 'Active')");

        migrationBuilder.CreateIndex(
            name: "IX_memberships_token_hash",
            schema: "organisation",
            table: "memberships",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_memberships_pharmacy_id_email",
            schema: "organisation",
            table: "memberships");

        migrationBuilder.DropIndex(
            name: "IX_memberships_token_hash",
            schema: "organisation",
            table: "memberships");
    }
}
