using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

public static class SampleMappings
{
    public static CertificationSampleDto ToDto(this Sample sample)
    {
        Ensure.That(sample).IsNotNull();

        return new CertificationSampleDto(sample.Id.Value, sample.Name.Value);
    }
}
