using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Compliance;

public class ComplianceEventsTests
{
    [Fact]
    public void CourseAssignedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new CourseAssignedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void AssignmentCompletedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new AssignmentCompletedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateTimeOffset(2027, 1, 5, 14, 15, 0, TimeSpan.FromHours(1)),
            true));
    }

    [Fact]
    public void AssignmentFollowUpRequestedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new AssignmentFollowUpRequestedV1(Guid.CreateVersion7()));
    }
}
