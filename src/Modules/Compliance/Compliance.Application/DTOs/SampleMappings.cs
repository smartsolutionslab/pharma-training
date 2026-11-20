using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class SampleMappings
{
    public static ComplianceSampleDto ToDto(this Sample sample)
    {
        Ensure.That(sample).IsNotNull();

        return new ComplianceSampleDto(sample.Id.Value, sample.Name.Value);
    }
}
