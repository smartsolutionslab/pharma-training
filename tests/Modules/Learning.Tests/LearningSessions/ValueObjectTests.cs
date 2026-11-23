using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

public class ValueObjectTests
{
    [Theory]
    [InlineData(nameof(LearningSessionId))]
    [InlineData(nameof(PersonId))]
    [InlineData(nameof(CourseId))]
    [InlineData(nameof(CourseVersionId))]
    [InlineData(nameof(ChapterId))]
    public void Every_identity_refuses_the_empty_guid(string identity) =>
        Should.Throw<GuardException>(FromEmpty(identity));

    [Fact]
    public void An_exhaustion_notice_is_never_empty() =>
        Should.Throw<GuardException>(() => ExhaustionNotice.From(Guid.Empty));

    [Fact]
    public void An_exhaustion_notice_keeps_the_identifier_it_was_made_from()
    {
        var identifier = Guid.CreateVersion7();

        ExhaustionNotice.From(identifier).Value.ShouldBe(identifier);
        ExhaustionNotice.From(identifier).ShouldBe(ExhaustionNotice.From(identifier));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_outside_0_to_100_is_refused(int value) =>
        Should.Throw<GuardException>(() => ProgressPercent.From(value));

    [Fact]
    public void Progress_compares_by_its_value()
    {
        (ProgressPercent.From(60) > ProgressPercent.From(40)).ShouldBeTrue();
        (ProgressPercent.From(60) <= ProgressPercent.From(60)).ShouldBeTrue();
        ProgressPercent.None.Value.ShouldBe(0);
    }

    [Fact]
    public void Two_spellings_of_one_instant_are_the_same_timestamp() =>
        Timestamp.From(new DateTimeOffset(2027, 3, 1, 11, 0, 0, TimeSpan.FromHours(2)))
            .ShouldBe(Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_video_counts_from_95_and_text_and_pdf_from_100()
    {
        CompletionRule.ThresholdFor(ChapterKind.Video).ShouldBe(ProgressPercent.From(95));
        CompletionRule.ThresholdFor(ChapterKind.Text).ShouldBe(ProgressPercent.Complete);
        CompletionRule.ThresholdFor(ChapterKind.Pdf).ShouldBe(ProgressPercent.Complete);
    }

    private static Action FromEmpty(string identity) =>
        identity switch
        {
            nameof(LearningSessionId) => () => LearningSessionId.From(Guid.Empty),
            nameof(PersonId) => () => PersonId.From(Guid.Empty),
            nameof(CourseId) => () => CourseId.From(Guid.Empty),
            nameof(CourseVersionId) => () => CourseVersionId.From(Guid.Empty),
            nameof(ChapterId) => () => ChapterId.From(Guid.Empty),
            _ => throw new InvalidOperationException($"{identity} is no identity of the session."),
        };
}
