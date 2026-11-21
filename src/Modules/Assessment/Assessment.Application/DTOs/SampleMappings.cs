using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

public static class SampleMappings
{
    public static AssessmentSampleDto ToDto(this SampleSummary sample)
    {
        Ensure.That(sample).IsNotNull();

        return new AssessmentSampleDto(sample.Id, sample.Name);
    }
}
