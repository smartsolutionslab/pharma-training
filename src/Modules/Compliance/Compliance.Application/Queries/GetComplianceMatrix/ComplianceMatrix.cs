using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// The team matrix before it is paged: every course with a non-cancelled assignment in the pharmacy as a column,
/// whatever its learner; every given member as a row; a cell where the member holds a non-cancelled assignment of the
/// course, showing the latest; and the tally over every cell.
/// </summary>
public sealed class ComplianceMatrix
{
    private ComplianceMatrix(IReadOnlyList<ComplianceMatrixCourse> courses, IReadOnlyList<ComplianceMatrixRow> rows, AssignmentTally tally)
    {
        Courses = courses;
        Rows = rows;
        Tally = tally;
    }

    /// <summary>The columns, by title, then by course id.</summary>
    public IReadOnlyList<ComplianceMatrixCourse> Courses { get; }

    /// <summary>Every row, by name, then by person id.</summary>
    public IReadOnlyList<ComplianceMatrixRow> Rows { get; }

    /// <summary>Over every cell of every row.</summary>
    public AssignmentTally Tally { get; }

    /// <summary>
    /// The matrix of the members, already one per person and filtered, over every assignment in the pharmacy; a course
    /// without a published copy is left out everywhere.
    /// </summary>
    public static ComplianceMatrix Of(
        IReadOnlyCollection<CurrentMembership> members,
        IReadOnlyCollection<Assignment> assignments,
        IReadOnlyCollection<PublishedCourse> courses,
        CalendarDate today)
    {
        Ensure.That(members).IsNotNull();
        Ensure.That(assignments).IsNotNull();
        Ensure.That(courses).IsNotNull();
        Ensure.That(today).IsNotNull();

        var assigned = assignments
            .Where(assignment => assignment.Status != AssignmentStatus.Cancelled)
            .Select(assignment => assignment.Course)
            .ToHashSet();
        List<PublishedCourse> columns =
        [
            .. courses
                .Where(course => assigned.Contains(course.Course))
                .DistinctBy(course => course.Course)
                .OrderBy(course => course.Title.Value, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(course => course.Course.Value),
        ];

        var byLearnerAndCourse = assignments.ToLookup(assignment => (assignment.Learner, assignment.Course));
        List<ComplianceMatrixRow> rows =
        [
            .. members
                .OrderBy(member => member.Name.Value, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(member => member.Person.Value)
                .Select(member => new ComplianceMatrixRow(member, CellsOf(member.Person, columns, byLearnerAndCourse, today))),
        ];

        var cells = rows.SelectMany(row => row.Cells).ToList();
        List<ComplianceMatrixCourse> tallied =
        [
            .. columns.Select(column => new ComplianceMatrixCourse(
                column.Course,
                column.Title,
                AssignmentTally.Of(cells.Where(cell => cell.Course == column.Course), today))),
        ];

        return new ComplianceMatrix(tallied, rows, AssignmentTally.Of(cells, today));
    }

    private static List<ComplianceMatrixCell> CellsOf(
        PersonId person,
        IReadOnlyList<PublishedCourse> columns,
        ILookup<(PersonId Learner, CourseId Course), Assignment> byLearnerAndCourse,
        CalendarDate today)
    {
        List<ComplianceMatrixCell> cells = [];
        foreach (var column in columns)
        {
            var held = byLearnerAndCourse[(person, column.Course)].Where(assignment => assignment.Status != AssignmentStatus.Cancelled).ToList();
            if (held.Count == 0)
            {
                continue;
            }

            var shown = held.OrderByDescending(assignment => assignment.AssignedAt).ThenByDescending(assignment => assignment.Id.Value).First();
            var lastCompleted = held
                .Where(assignment => assignment.Status == AssignmentStatus.Completed)
                .Select(assignment => assignment.CompletedAt)
                .OfType<Timestamp>()
                .Order()
                .LastOrDefault();
            var state = shown.StateOn(today);
            var validUntil = state is AssignmentState.Completed or AssignmentState.CompletedLate && shown.CompletedAt is { } completedAt
                ? CalendarDate.From(completedAt.DayInGermany().Value.AddMonths(column.Latest.Validity.Value))
                : null;
            cells.Add(new ComplianceMatrixCell(column.Course, shown.Id, state, shown.DueDate, lastCompleted?.DayInGermany(), validUntil));
        }

        return cells;
    }
}
