using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

public static class SampleMappings
{
    public static EditorialSampleDto ToDto(this Sample sample)
    {
        Ensure.That(sample).IsNotNull();

        return new EditorialSampleDto(sample.Id.Value, sample.Name.Value);
    }
}
