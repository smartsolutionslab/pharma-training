using SmartSolutionsLab.PharmaTraining.Contracts.Certification;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Certification;

public class CertificationEventsTests
{
    [Fact]
    public void CertificateIssuedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new CertificateIssuedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateTimeOffset(2026, 11, 21, 10, 46, 0, TimeSpan.FromHours(1)),
            new DateOnly(2028, 11, 21)));
    }
}
