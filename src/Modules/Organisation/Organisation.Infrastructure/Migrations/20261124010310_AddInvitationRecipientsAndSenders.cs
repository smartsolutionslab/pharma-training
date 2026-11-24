using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124010310_AddInvitationRecipientsAndSenders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "invitation_recipients",
            schema: "organisation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                last_mailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invitation_recipients", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "invitation_senders",
            schema: "organisation",
            columns: table => new
            {
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                mail_day = table.Column<DateOnly>(type: "date", nullable: false),
                mails_that_day = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invitation_senders", x => x.person_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invitation_recipients",
            schema: "organisation");

        migrationBuilder.DropTable(
            name: "invitation_senders",
            schema: "organisation");
    }
}
