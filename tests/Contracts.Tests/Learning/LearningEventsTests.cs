using SmartSolutionsLab.PharmaTraining.Contracts.Learning;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Learning;

public class LearningEventsTests
{
    [Fact]
    public void LearningSessionStartedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new LearningSessionStartedV1(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    [Fact]
    public void ContentCompletedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new ContentCompletedV1(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()));
    }
}
