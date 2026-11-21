using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

public static class SampleMappings
{
    public static CatalogSampleDto ToDto(this Sample sample)
    {
        Ensure.That(sample).IsNotNull();

        return new CatalogSampleDto(sample.Id, sample.Name);
    }
}
