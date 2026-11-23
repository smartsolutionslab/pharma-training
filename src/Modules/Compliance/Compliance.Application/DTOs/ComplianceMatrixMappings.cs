using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class ComplianceMatrixMappings
{
    /// <summary>The matrix with one page of its rows; the columns and the tallies cover every row.</summary>
    public static ComplianceMatrixDto ToDto(this ComplianceMatrix matrix, Page page, PageSize pageSize)
    {
        Ensure.That(matrix).IsNotNull();
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var rows = matrix.Rows
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .Select(row => row.ToDto());
        return new ComplianceMatrixDto(
            [.. matrix.Courses.Select(course => course.ToDto())],
            new PagedResult<ComplianceMatrixRowDto>([.. rows], matrix.Rows.Count, page.Value, pageSize.Value),
            matrix.Tally.Assignments,
            matrix.Tally.Compliant,
            matrix.Tally.Overdue,
            matrix.Tally.ComplianceRate);
    }

    public static ComplianceMatrixCourseDto ToDto(this ComplianceMatrixCourse course)
    {
        Ensure.That(course).IsNotNull();

        return new ComplianceMatrixCourseDto(
            course.Course.Value,
            course.Title.Value,
            course.Tally.Assignments,
            course.Tally.Compliant,
            course.Tally.Overdue,
            course.Tally.ComplianceRate);
    }

    public static ComplianceMatrixRowDto ToDto(this ComplianceMatrixRow row)
    {
        Ensure.That(row).IsNotNull();

        return new ComplianceMatrixRowDto(
            row.Member.Person.Value,
            row.Member.Name.Value,
            row.Member.JobFunction.ToDto(),
            [.. row.Cells.Select(cell => cell.ToDto())]);
    }

    public static ComplianceMatrixCellDto ToDto(this ComplianceMatrixCell cell)
    {
        Ensure.That(cell).IsNotNull();

        return new ComplianceMatrixCellDto(cell.Course.Value, cell.Assignment.Value, cell.State.ToDto(), cell.DueDate.Value, cell.LastCompletedOn?.Value, cell.ValidUntil?.Value);
    }
}
