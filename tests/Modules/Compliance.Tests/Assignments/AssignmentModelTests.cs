using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// Compliance's model of an assignment, without a database: "at most one active assignment per learner, pharmacy and
/// course" is backed twice, by the handler and by a partial unique index; the aggregate carries a row version
/// (docs/adr/0002); its history is one JSON column, so #30 needs no migration.
/// </summary>
public sealed class AssignmentModelTests : IDisposable
{
    private readonly ComplianceDbContext context = new ComplianceDbContextFactory().CreateDbContext([]);

    [Fact]
    public void At_most_one_active_assignment_per_learner_pharmacy_and_course()
    {
        var assignment = AssignmentType();

        var index = assignment.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Assignment.Learner), nameof(Assignment.Pharmacy), nameof(Assignment.Course)]));

        index.ShouldNotBeNull("no index on (Learner, Pharmacy, Course)");
        index.IsUnique.ShouldBeTrue();
        index.GetDatabaseName().ShouldBe("ux_assignments_active_learner_pharmacy_course");
        index.GetFilter().ShouldBe("status IN ('Open', 'InProgress')");
    }

    [Fact]
    public void An_assignment_carries_a_row_version()
    {
        var assignment = AssignmentType();

        assignment.HasRowVersion().ShouldBeTrue();
        assignment.FindProperty(RowVersion.PropertyName).ShouldNotBeNull().GetColumnName().ShouldBe("xmin");
    }

    [Fact]
    public void The_history_is_stored_as_json_in_one_column()
    {
        var assignment = AssignmentType();

        // Mapped through the property or, since EF Core refuses a complex collection typed IReadOnlyList, its backing field.
        var history = assignment.GetComplexProperties()
            .SingleOrDefault(candidate => string.Equals(candidate.Name, nameof(Assignment.History), StringComparison.OrdinalIgnoreCase));

        history.ShouldNotBeNull("the history is no complex property");
        history.IsCollection.ShouldBeTrue();
        history.ComplexType.IsMappedToJson().ShouldBeTrue();
        history.ComplexType.GetContainerColumnName().ShouldBe("history");
        Table().FindColumn("history").ShouldNotBeNull().StoreType.ShouldBe("jsonb");
    }

    [Fact]
    public void The_assignments_live_in_the_modules_schema_with_their_lookups_by_pharmacy_and_learner()
    {
        var assignment = AssignmentType();

        assignment.GetTableName().ShouldBe("assignments");
        assignment.GetSchema().ShouldBe(ComplianceDbContext.Schema);
        assignment.FindPrimaryKey().ShouldNotBeNull().GetName().ShouldBe("pk_assignments");
        IndexName(assignment, nameof(Assignment.Pharmacy)).ShouldBe("ix_assignments_pharmacy_id");
        IndexName(assignment, nameof(Assignment.Learner)).ShouldBe("ix_assignments_learner_id");
    }

    [Theory]
    [InlineData("status", "character varying(16)", false)]
    [InlineData("test_access", "character varying(32)", false)]
    [InlineData("learner_id", "uuid", false)]
    [InlineData("pharmacy_id", "uuid", false)]
    [InlineData("course_id", "uuid", false)]
    [InlineData("due_date", "date", false)]
    [InlineData("recurrence_months", "integer", true)]
    [InlineData("assigned_by", "uuid", false)]
    [InlineData("assigned_at", "timestamp with time zone", false)]
    [InlineData("started_at", "timestamp with time zone", true)]
    [InlineData("completed_at", "timestamp with time zone", true)]
    [InlineData("certificate_id", "uuid", true)]
    public void Each_column_has_its_type_and_nullability(string name, string storeType, bool nullable)
    {
        var column = Table().FindColumn(name);

        column.ShouldNotBeNull($"no column {name}");
        column.StoreType.ShouldBe(storeType);
        column.IsNullable.ShouldBe(nullable);
    }

    [Fact]
    public void The_time_of_the_last_lock_or_unlock_is_a_nullable_column()
    {
        var property = AssignmentType().FindProperty(nameof(Assignment.TestAccessChangedAt));

        property.ShouldNotBeNull($"{nameof(Assignment.TestAccessChangedAt)} is not mapped");
        property.GetColumnName().ShouldBe("test_access_changed_at");
        var column = Table().FindColumn("test_access_changed_at");
        column.ShouldNotBeNull("no column test_access_changed_at");
        column.StoreType.ShouldBe("timestamp with time zone");
        column.IsNullable.ShouldBeTrue();
    }

    public void Dispose() => context.Dispose();

    private IEntityType AssignmentType()
    {
        var assignment = context.Model.FindEntityType(typeof(Assignment));
        assignment.ShouldNotBeNull("Assignment is no entity type of ComplianceDbContext");
        return assignment;
    }

    private ITable Table() =>
        context.Model.GetRelationalModel().Tables.SingleOrDefault(table => table.Name == "assignments" && table.Schema == ComplianceDbContext.Schema)
            .ShouldNotBeNull("no table compliance.assignments");

    private static string? IndexName(IEntityType assignment, string property) =>
        assignment.GetIndexes()
            .SingleOrDefault(candidate => candidate.Properties.Select(indexed => indexed.Name).SequenceEqual([property]))
            .ShouldNotBeNull($"no index on {property} alone")
            .GetDatabaseName();
}
