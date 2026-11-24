using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123185814_AddInvitationMailLimits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "invitation_mail_day",
            schema: "organisation",
            table: "pharmacies",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "invitation_mails_that_day",
            schema: "organisation",
            table: "pharmacies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "invitation_requested_at",
            schema: "organisation",
            table: "memberships",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "invitation_mail_day",
            schema: "organisation",
            table: "pharmacies");

        migrationBuilder.DropColumn(
            name: "invitation_mails_that_day",
            schema: "organisation",
            table: "pharmacies");

        migrationBuilder.DropColumn(
            name: "invitation_requested_at",
            schema: "organisation",
            table: "memberships");
    }
}
