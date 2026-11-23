using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class ExaminationGuardTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>An attempt without questions could never be scored: the score of none of none is no percentage.</summary>
    [Fact]
    public void An_attempt_without_questions_is_refused_before_it_starts()
    {
        var examination = Examination.Open(
            ExaminationId.New(),
            LearningSessionId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            Now);

        Should.Throw<GuardException>(() => examination.StartAttempt(AttemptId.New(), [], Now));

        ((IEventSourced)examination).PendingEvents.ShouldHaveSingleItem();
        examination.Status.ShouldBe(ExaminationStatus.Unlocked);
    }
}
