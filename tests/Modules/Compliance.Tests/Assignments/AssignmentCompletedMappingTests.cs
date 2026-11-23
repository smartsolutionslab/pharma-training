using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>A completed assignment is announced as AssignmentCompletedV1: its identities, the instant of the certificate, and whether that missed the due date.</summary>
public class AssignmentCompletedMappingTests
{
    private static readonly Timestamp AssignedAt = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
    private static readonly DueDate Due = DueDate.From(new DateOnly(2027, 3, 31));

    [Fact]
    public void A_completion_in_time_maps_every_field_and_is_not_late()
    {
        var completedAt = Timestamp.From(new DateTimeOffset(2027, 3, 31, 21, 0, 0, TimeSpan.Zero));
        var (assignment, certificate) = Completed(completedAt);

        var announced = assignment.DomainEvents.OfType<AssignmentCompleted>().ShouldHaveSingleItem().ToIntegrationEvent();

        announced.AssignmentId.ShouldBe(assignment.Id.Value);
        announced.PersonId.ShouldBe(assignment.Learner.Value);
        announced.PharmacyId.ShouldBe(assignment.Pharmacy.Value);
        announced.CourseId.ShouldBe(assignment.Course.Value);
        announced.CertificateId.ShouldBe(certificate.Value);
        announced.CompletedAt.ShouldBe(completedAt.Value);
        announced.Late.ShouldBeFalse();
    }

    [Fact]
    public void A_completion_on_a_day_in_Germany_after_the_due_date_is_late()
    {
        var completedAt = Timestamp.From(new DateTimeOffset(2027, 3, 31, 22, 0, 0, TimeSpan.Zero));
        var (assignment, certificate) = Completed(completedAt);

        var announced = assignment.DomainEvents.OfType<AssignmentCompleted>().ShouldHaveSingleItem().ToIntegrationEvent();

        announced.CertificateId.ShouldBe(certificate.Value);
        announced.CompletedAt.ShouldBe(completedAt.Value);
        announced.Late.ShouldBeTrue();
    }

    [Fact]
    public void No_completion_is_refused()
    {
        Should.Throw<GuardException>(() => ((AssignmentCompleted)null!).ToIntegrationEvent());
    }

    private static (Assignment Assignment, CertificateId Certificate) Completed(Timestamp completedAt)
    {
        var assignment = Assignment.Assign(
            AssignmentId.New(),
            PersonId.From(Guid.CreateVersion7()),
            PharmacyId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            Due,
            null,
            PersonId.From(Guid.CreateVersion7()),
            AssignedAt).Value;
        var certificate = CertificateId.From(Guid.CreateVersion7());
        assignment.Complete(certificate, completedAt).IsSuccess.ShouldBeTrue();
        return (assignment, certificate);
    }
}
