using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// The assignment's own refusals, a closed union, mapped by <c>AssignmentFailures.From(AssignmentError)</c> with a switch
/// that has no discard arm, each keeping the domain's code; and the active assignment the Application finds across
/// aggregates, a conflict.
/// </summary>
public class AssignmentFailuresTests
{
    [Theory]
    [InlineData("DueDateInThePast", HttpStatusCode.UnprocessableEntity, "compliance.due-date-in-the-past")]
    [InlineData("AssignmentNotActive", HttpStatusCode.Conflict, "compliance.assignment-not-active")]
    [InlineData("CertificatePredatesAssignment", HttpStatusCode.Conflict, "compliance.certificate-predates-assignment")]
    public void Each_assignment_error_maps_to_its_status(string name, HttpStatusCode status, string code)
    {
        var error = Case(name);

        var mapped = AssignmentFailures.From(error);

        mapped.Status.ShouldBe(status);
        mapped.Code.ShouldBe(code);
        mapped.Message.ShouldBe(error.Value.ShouldBeAssignableTo<DomainError>().ShouldNotBeNull().Message);
    }

    [Fact]
    public void An_active_assignment_is_a_conflict()
    {
        var error = AssignmentErrors.AlreadyActive(PersonId.From(Guid.CreateVersion7()), CourseId.From(Guid.CreateVersion7()));

        var mapped = AssignmentFailures.AlreadyActive(error);

        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
        mapped.Code.ShouldBe("compliance.assignment-already-active");
        mapped.Message.ShouldBe(error.Message);
    }

    [Fact]
    public void A_job_function_the_enum_does_not_have_is_the_callers_mistake()
    {
        var error = AssignmentFailures.InvalidJobFunction();

        error.Code.ShouldBe("compliance.invalid-job-function");
        error.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void An_assignment_the_pharmacy_does_not_have_is_not_found()
    {
        var id = Guid.CreateVersion7();

        var error = AssignmentFailures.AssignmentNotFound(id);

        error.Code.ShouldBe("compliance.assignment-not-found");
        error.Status.ShouldBe(HttpStatusCode.NotFound);
        error.Message.ShouldBe("This pharmacy has no such assignment.");
        error.Message.ShouldNotContain(id.ToString());
    }

    [Fact]
    public void A_change_reason_longer_than_its_maximum_is_the_callers_mistake()
    {
        var error = AssignmentFailures.InvalidChangeReason();

        error.Code.ShouldBe("compliance.invalid-change-reason");
        error.Status.ShouldBe(HttpStatusCode.BadRequest);
        error.Message.ShouldContain(ChangeReason.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static AssignmentError Case(string name) => name switch
    {
        "DueDateInThePast" => AssignmentErrors.DueDateInThePast(DueDate.From(new DateOnly(2027, 2, 28))),
        "AssignmentNotActive" => AssignmentErrors.NotActive(AssignmentId.New(), AssignmentStatus.Completed),
        "CertificatePredatesAssignment" => AssignmentErrors.CertificatePredatesAssignment(AssignmentId.New(), CertificateId.From(Guid.CreateVersion7())),
        _ => throw new InvalidOperationException($"No assignment error is named {name}."),
    };
}
