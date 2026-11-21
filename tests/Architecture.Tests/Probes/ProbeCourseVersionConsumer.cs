using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A consumer of the course's content without its questions, which any module may have.</summary>
public static class ProbeCourseVersionConsumer
{
    public static void Handle(CourseVersionPublishedV1 message) => Ensure.That(message).IsNotNull();
}
