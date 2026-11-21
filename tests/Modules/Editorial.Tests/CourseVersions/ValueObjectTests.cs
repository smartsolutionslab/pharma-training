using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>The guards of every value and identity of a course version, and how a caller's text becomes one.</summary>
public class ValueObjectTests
{
    [Fact]
    public void A_new_identity_is_a_time_ordered_guid_and_from_keeps_the_guid_it_is_given()
    {
        var guid = Guid.CreateVersion7();

        CourseId.New().Value.Version.ShouldBe(7);
        CourseVersionId.New().Value.Version.ShouldBe(7);
        ChapterId.New().Value.Version.ShouldBe(7);
        QuestionId.New().Value.Version.ShouldBe(7);
        AnswerOptionId.New().Value.Version.ShouldBe(7);
        CourseId.From(guid).Value.ShouldBe(guid);
        CourseVersionId.From(guid).Value.ShouldBe(guid);
        ChapterId.From(guid).Value.ShouldBe(guid);
        QuestionId.From(guid).Value.ShouldBe(guid);
        AnswerOptionId.From(guid).Value.ShouldBe(guid);
        PersonId.From(guid).Value.ShouldBe(guid);
        ChapterId.From(guid).ShouldBe(ChapterId.From(guid));
    }

    [Fact]
    public void The_empty_guid_is_no_identity()
    {
        Should.Throw<GuardException>(() => CourseId.From(Guid.Empty));
        Should.Throw<GuardException>(() => CourseVersionId.From(Guid.Empty));
        Should.Throw<GuardException>(() => ChapterId.From(Guid.Empty));
        Should.Throw<GuardException>(() => QuestionId.From(Guid.Empty));
        Should.Throw<GuardException>(() => AnswerOptionId.From(Guid.Empty));
        Should.Throw<GuardException>(() => PersonId.From(Guid.Empty));
    }

    [Fact]
    public void Two_spellings_of_one_instant_are_one_timestamp_kept_in_utc()
    {
        var utc = new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var berlin = new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.FromHours(1));

        Timestamp.From(berlin).ShouldBe(Timestamp.From(utc));
        Timestamp.From(berlin).Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(100)]
    public void A_score_from_0_to_100_percent_is_a_score(int value) =>
        ScorePercent.From(value).Value.ShouldBe(value);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_score_outside_0_to_100_percent_is_refused(int value) =>
        Should.Throw<GuardException>(() => ScorePercent.From(value));

    [Fact]
    public void A_recurrence_interval_is_1_to_60_months_and_yearly_is_12()
    {
        RecurrenceInterval.Yearly.Value.ShouldBe(12);
        RecurrenceInterval.From(1).Value.ShouldBe(1);
        RecurrenceInterval.From(60).Value.ShouldBe(60);
        Should.Throw<GuardException>(() => RecurrenceInterval.From(0));
        Should.Throw<GuardException>(() => RecurrenceInterval.From(61));
    }

    [Fact]
    public void A_question_count_is_0_to_50_and_10_by_default()
    {
        QuestionCount.Default.Value.ShouldBe(10);
        QuestionCount.From(0).Value.ShouldBe(0);
        QuestionCount.From(50).Value.ShouldBe(50);
        Should.Throw<GuardException>(() => QuestionCount.From(-1));
        Should.Throw<GuardException>(() => QuestionCount.From(51));
    }

    [Fact]
    public void A_version_number_starts_at_1_and_counts_up()
    {
        VersionNumber.First.Value.ShouldBe(1);
        VersionNumber.First.Next().ShouldBe(VersionNumber.From(2));
        Should.Throw<GuardException>(() => VersionNumber.From(0));
    }

    [Fact]
    public void A_course_title_is_trimmed_and_at_most_150_characters()
    {
        CourseTitle.MaxLength.ShouldBe(150);
        CourseTitle.From("  Hygiene 2027 ").Value.ShouldBe("Hygiene 2027");
        CourseTitle.From(new string('a', 150)).Value.Length.ShouldBe(150);
        Should.Throw<GuardException>(() => CourseTitle.From(new string('a', 151)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_course_title_is_refused(string value) =>
        Should.Throw<GuardException>(() => CourseTitle.From(value));

    [Fact]
    public void A_callers_course_title_becomes_a_trimmed_title()
    {
        CourseTitle.TryFrom("  Hygiene 2027 ", out var title).ShouldBeTrue();
        title.ShouldBe(CourseTitle.From("Hygiene 2027"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_course_title_is_no_title_and_no_exception(string? value)
    {
        CourseTitle.TryFrom(value, out var title).ShouldBeFalse();
        title.ShouldBeNull();
    }

    [Fact]
    public void A_callers_course_title_is_no_title_exactly_when_from_refuses_it()
    {
        var padded = $" {new string('a', 150)} ";

        CourseTitle.TryFrom(new string('a', 151), out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => CourseTitle.From(padded));
        CourseTitle.TryFrom(padded, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_chapter_title_is_trimmed_and_at_most_150_characters()
    {
        ChapterTitle.MaxLength.ShouldBe(150);
        ChapterTitle.From("  Händehygiene ").Value.ShouldBe("Händehygiene");
        ChapterTitle.From(new string('a', 150)).Value.Length.ShouldBe(150);
        Should.Throw<GuardException>(() => ChapterTitle.From(new string('a', 151)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_chapter_title_is_refused(string value) =>
        Should.Throw<GuardException>(() => ChapterTitle.From(value));

    [Fact]
    public void A_callers_chapter_title_becomes_a_trimmed_title()
    {
        ChapterTitle.TryFrom(" Händehygiene  ", out var title).ShouldBeTrue();
        title.ShouldBe(ChapterTitle.From("Händehygiene"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_chapter_title_is_no_title_and_no_exception(string? value)
    {
        ChapterTitle.TryFrom(value, out var title).ShouldBeFalse();
        title.ShouldBeNull();
    }

    [Fact]
    public void A_callers_chapter_title_is_no_title_exactly_when_from_refuses_it()
    {
        var padded = $" {new string('a', 150)} ";

        ChapterTitle.TryFrom(new string('a', 151), out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => ChapterTitle.From(padded));
        ChapterTitle.TryFrom(padded, out _).ShouldBeFalse();
    }

    [Fact]
    public void Markdown_text_is_kept_as_written_up_to_200000_characters()
    {
        MarkdownText.MaxLength.ShouldBe(200_000);
        MarkdownText.From("  # Händehygiene\n\nVor und nach …  ").Value.ShouldBe("  # Händehygiene\n\nVor und nach …  ");
        MarkdownText.From(new string('a', 200_000)).Value.Length.ShouldBe(200_000);
        Should.Throw<GuardException>(() => MarkdownText.From(new string('a', 200_001)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n ")]
    public void Blank_markdown_text_is_refused(string value) =>
        Should.Throw<GuardException>(() => MarkdownText.From(value));

    [Fact]
    public void A_callers_markdown_text_is_kept_as_written()
    {
        MarkdownText.TryFrom(" - Hände waschen\n", out var text).ShouldBeTrue();
        text.ShouldBe(MarkdownText.From(" - Hände waschen\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    public void A_callers_blank_markdown_text_is_no_text_and_no_exception(string? value)
    {
        MarkdownText.TryFrom(value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_callers_markdown_text_longer_than_the_maximum_is_no_text() =>
        MarkdownText.TryFrom(new string('a', 200_001), out _).ShouldBeFalse();

    [Fact]
    public void A_blob_reference_is_a_name_of_at_most_1024_characters_and_never_a_url()
    {
        BlobReference.From("courses/hygiene.pdf").Value.ShouldBe("courses/hygiene.pdf");
        BlobReference.From(new string('a', 1024)).Value.Length.ShouldBe(1024);
        Should.Throw<GuardException>(() => BlobReference.From(new string('a', 1025)));
        Should.Throw<GuardException>(() => BlobReference.From("https://store.example/hygiene.pdf?sig=secret"));
        Should.Throw<GuardException>(() => BlobReference.From("  "));
    }

    [Fact]
    public void A_video_lasts_from_one_second_to_four_hours()
    {
        VideoDuration.From(1).Value.ShouldBe(1);
        VideoDuration.From(4 * 60 * 60).Value.ShouldBe(4 * 60 * 60);
        Should.Throw<GuardException>(() => VideoDuration.From(0));
        Should.Throw<GuardException>(() => VideoDuration.From((4 * 60 * 60) + 1));
    }

    [Fact]
    public void A_question_text_is_trimmed_and_at_most_1000_characters()
    {
        QuestionText.From(" Wie oft? ").Value.ShouldBe("Wie oft?");
        QuestionText.From(new string('a', 1000)).Value.Length.ShouldBe(1000);
        Should.Throw<GuardException>(() => QuestionText.From(new string('a', 1001)));
        Should.Throw<GuardException>(() => QuestionText.From(" "));
    }

    [Fact]
    public void An_answer_text_is_trimmed_and_at_most_400_characters()
    {
        AnswerText.From(" Täglich ").Value.ShouldBe("Täglich");
        AnswerText.From(new string('a', 400)).Value.Length.ShouldBe(400);
        Should.Throw<GuardException>(() => AnswerText.From(new string('a', 401)));
        Should.Throw<GuardException>(() => AnswerText.From(" "));
    }

    [Fact]
    public void The_default_blueprint_asks_ten_questions_passes_at_80_percent_and_is_valid_for_a_year()
    {
        var blueprint = TestBlueprint.Default;

        blueprint.Questions.ShouldBe(QuestionCount.Default);
        blueprint.PassingScore.ShouldBe(ScorePercent.From(80));
        blueprint.Validity.ShouldBe(RecurrenceInterval.Yearly);
        blueprint.ShouldBe(new TestBlueprint(QuestionCount.From(10), ScorePercent.From(80), RecurrenceInterval.From(12)));
    }

    [Fact]
    public void A_blueprint_without_questions_is_refused() =>
        Should.Throw<GuardException>(() => new TestBlueprint(QuestionCount.From(0), ScorePercent.From(80), RecurrenceInterval.Yearly));
}
