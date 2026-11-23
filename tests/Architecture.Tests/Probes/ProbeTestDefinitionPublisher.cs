using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>Code that creates Editorial's test definition, as a module other than Editorial would to publish it.</summary>
public static class ProbeTestDefinitionPublisher
{
    public static TestDefinitionPublishedV1 Create() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), 80, 3, []);

    public static Func<TestDefinitionPublishedV1> Later() => () => new(Guid.CreateVersion7(), Guid.CreateVersion7(), 80, 3, []);
}
