using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

public class LearningSessionIdTests
{
    private static readonly PersonId Learner = PersonId.From(Guid.CreateVersion7());
    private static readonly CourseVersionId Version = CourseVersionId.From(Guid.CreateVersion7());

    [Fact]
    public void The_same_learner_version_and_run_derive_the_same_id() =>
        LearningSessionId.For(Learner, Version, SessionRun.First).ShouldBe(LearningSessionId.For(Learner, Version, SessionRun.First));

    [Fact]
    public void Another_run_derives_another_id() =>
        LearningSessionId.For(Learner, Version, SessionRun.From(1)).ShouldNotBe(LearningSessionId.For(Learner, Version, SessionRun.From(2)));

    [Fact]
    public void Another_learner_or_another_version_derives_another_id()
    {
        var id = LearningSessionId.For(Learner, Version, SessionRun.First);

        LearningSessionId.For(PersonId.From(Guid.CreateVersion7()), Version, SessionRun.First).ShouldNotBe(id);
        LearningSessionId.For(Learner, CourseVersionId.From(Guid.CreateVersion7()), SessionRun.First).ShouldNotBe(id);
    }

    [Fact]
    public void A_derived_id_is_a_version_8_rfc_9562_uuid()
    {
        var id = LearningSessionId.For(Learner, Version, SessionRun.First);

        id.Value.Version.ShouldBe(8);
        (id.Value.Variant & 0b1100).ShouldBe(0b1000, "the variant bits of an RFC 9562 UUID are 10xx");
        id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void A_run_below_1_is_refused() =>
        Should.Throw<GuardException>(() => SessionRun.From(0));

    [Fact]
    public void The_next_run_of_the_first_is_the_second() =>
        SessionRun.First.Next().ShouldBe(SessionRun.From(2));
}
