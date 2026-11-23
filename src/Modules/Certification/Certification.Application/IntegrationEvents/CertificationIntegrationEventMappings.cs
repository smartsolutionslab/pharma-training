using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.IntegrationEvents;

/// <summary>How the module's domain events are announced to other modules; pure, no I/O.</summary>
public static class CertificationIntegrationEventMappings
{
    public static CertificateIssuedV1 ToIntegrationEvent(this CertificateIssued issued)
    {
        Ensure.That(issued).IsNotNull();

        return new CertificateIssuedV1(issued.Certificate.Value, issued.Holder.Value, issued.Course.Value, issued.CourseVersion.Value, issued.IssuedAt.Value, issued.ValidUntil.Value);
    }
}
