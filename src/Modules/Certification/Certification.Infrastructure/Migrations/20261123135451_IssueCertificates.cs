using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123135451_IssueCertificates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "certificates",
            schema: "certification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                holder_id = table.Column<Guid>(type: "uuid", nullable: false),
                holder_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                score_percent = table.Column<int>(type: "integer", nullable: false),
                issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                verification_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                document_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                document_location = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_certificates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "course_versions",
            schema: "certification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                validity_months = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_course_versions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "membership_endings",
            schema: "certification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_endings", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "memberships",
            schema: "certification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                pharmacy_id = table.Column<Guid>(type: "uuid", nullable: false),
                holder_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_memberships", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_certificates_attempt_id",
            schema: "certification",
            table: "certificates",
            column: "attempt_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_certificates_verification_code",
            schema: "certification",
            table: "certificates",
            column: "verification_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_memberships_person_id",
            schema: "certification",
            table: "memberships",
            column: "person_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "certificates",
            schema: "certification");

        migrationBuilder.DropTable(
            name: "course_versions",
            schema: "certification");

        migrationBuilder.DropTable(
            name: "membership_endings",
            schema: "certification");

        migrationBuilder.DropTable(
            name: "memberships",
            schema: "certification");
    }
}
