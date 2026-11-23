using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123223745_AddLearnerProgress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "issued_certificates",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                learner_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_issued_certificates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "learning_session_starts",
            schema: "compliance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                learner_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_learning_session_starts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_issued_certificates_learner_id_course_id",
            schema: "compliance",
            table: "issued_certificates",
            columns: new[] { "learner_id", "course_id" });

        migrationBuilder.CreateIndex(
            name: "ix_learning_session_starts_learner_id_course_id",
            schema: "compliance",
            table: "learning_session_starts",
            columns: new[] { "learner_id", "course_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "issued_certificates",
            schema: "compliance");

        migrationBuilder.DropTable(
            name: "learning_session_starts",
            schema: "compliance");
    }
}
