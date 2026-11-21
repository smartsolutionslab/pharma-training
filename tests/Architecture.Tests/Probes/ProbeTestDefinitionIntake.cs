using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A base class, not named like a consumer, that handles the test definition for the consumers deriving from it.</summary>
public abstract class ProbeTestDefinitionIntake
{
    public void Handle(TestDefinitionPublishedV1 message) => Received = message;

    public TestDefinitionPublishedV1? Received { get; private set; }
}
