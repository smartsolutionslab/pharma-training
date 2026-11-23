using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.IntegrationEvents;

/// <summary>A certificate's issue is announced to other modules in one place, <c>CertificationIntegrationEventMappings</c>.</summary>
public class CertificationIntegrationEventMappingsTests
{
    [Fact]
    public void An_issued_certificate_maps_to_certificate_issued_v1_with_its_identities_instant_and_last_valid_day()
    {
        var certificate = CertificateId.New();
        var holder = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var version = CourseVersionId.From(Guid.CreateVersion7());
        var issuedAt = Timestamp.From(new DateTimeOffset(2027, 3, 1, 23, 30, 0, TimeSpan.Zero));
        var validUntil = CalendarDate.From(new DateOnly(2028, 3, 2));

        var announced = new CertificateIssued(certificate, holder, course, version, issuedAt, validUntil).ToIntegrationEvent();

        announced.CertificateId.ShouldBe(certificate.Value);
        announced.PersonId.ShouldBe(holder.Value);
        announced.CourseId.ShouldBe(course.Value);
        announced.CourseVersionId.ShouldBe(version.Value);
        announced.IssuedAt.ShouldBe(issuedAt.Value);
        announced.ValidUntil.ShouldBe(new DateOnly(2028, 3, 2));
    }

    [Fact]
    public void No_event_is_refused()
    {
        Should.Throw<GuardException>(() => ((CertificateIssued)null!).ToIntegrationEvent());
    }
}
