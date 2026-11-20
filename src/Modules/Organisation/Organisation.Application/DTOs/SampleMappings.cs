using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

public static class SampleMappings
{
    public static OrganisationSampleDto ToDto(this Sample sample)
    {
        Ensure.That(sample).IsNotNull();

        return new OrganisationSampleDto(sample.Id.Value, sample.Name.Value);
    }
}
