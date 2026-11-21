using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

public static class SampleMappings
{
    public static LearningSampleDto ToDto(this SampleSummary sample)
    {
        Ensure.That(sample).IsNotNull();

        return new LearningSampleDto(sample.Id, sample.Name);
    }
}
