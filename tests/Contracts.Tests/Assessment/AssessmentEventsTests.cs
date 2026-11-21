using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Assessment;

public class AssessmentEventsTests
{
    [Fact]
    public void TestPassedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new TestPassedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            90,
            new DateTimeOffset(2026, 11, 21, 10, 45, 0, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void TestFailedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new TestFailedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2));
    }

    [Fact]
    public void AttemptsExhaustedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new AttemptsExhaustedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [Guid.CreateVersion7(), Guid.CreateVersion7()]));
    }
}
