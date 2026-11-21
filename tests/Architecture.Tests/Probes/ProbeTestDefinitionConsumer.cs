using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A consumer of the test definition outside Assessment, which would receive every question's solution.</summary>
public static class ProbeTestDefinitionConsumer
{
    public static void Handle(TestDefinitionPublishedV1 message) => Ensure.That(message).IsNotNull();
}
