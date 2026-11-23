using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class CourseVersionFailuresTests
{
    private static readonly CourseVersionId Version = CourseVersionId.New();

    [Fact]
    public void What_a_caller_gets_wrong_is_a_bad_request_with_its_own_code()
    {
        CourseVersionFailures.InvalidCourseTitle().Code.ShouldBe("editorial.invalid-course-title");
        CourseVersionFailures.InvalidCourseTitle().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidChapterTitle().Code.ShouldBe("editorial.invalid-chapter-title");
        CourseVersionFailures.InvalidChapterTitle().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidChapterText().Code.ShouldBe("editorial.invalid-chapter-text");
        CourseVersionFailures.InvalidChapterText().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidChapterOrder().Code.ShouldBe("editorial.invalid-chapter-order");
        CourseVersionFailures.InvalidChapterOrder().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidCourseVersionStatus().Code.ShouldBe("editorial.invalid-status");
        CourseVersionFailures.InvalidCourseVersionStatus().Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void What_a_caller_gets_wrong_about_a_chapters_file_is_a_bad_request_with_its_own_code()
    {
        CourseVersionFailures.InvalidUploadKind().Code.ShouldBe("editorial.invalid-upload-kind");
        CourseVersionFailures.InvalidUploadKind().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidChapterKind().Code.ShouldBe("editorial.invalid-chapter-kind");
        CourseVersionFailures.InvalidChapterKind().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.ChapterContentDoesNotMatchKind().Code.ShouldBe("editorial.chapter-content-does-not-match-kind");
        CourseVersionFailures.ChapterContentDoesNotMatchKind().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidChapterFile().Code.ShouldBe("editorial.invalid-chapter-file");
        CourseVersionFailures.InvalidChapterFile().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.InvalidVideoDuration().Code.ShouldBe("editorial.invalid-video-duration");
        CourseVersionFailures.InvalidVideoDuration().Status.ShouldBe(HttpStatusCode.BadRequest);
        CourseVersionFailures.UploadNotAccepted().Code.ShouldBe("editorial.upload-not-accepted");
        CourseVersionFailures.UploadNotAccepted().Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_refused_upload_says_what_it_is_not_without_naming_the_signatures_checked()
    {
        string[] checkedDetails = ["%PDF", "ftyp", "byte", "isom", "iso2", "mp41", "mp42", "avc1", "M4V", "dash"];

        var refused = CourseVersionFailures.UploadNotAccepted();

        refused.Code.ShouldBe("editorial.upload-not-accepted");
        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Message.ShouldNotBeNullOrWhiteSpace();
        checkedDetails
            .Where(detail => refused.Message.Contains(detail, StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty($"the message tells the uploader the file was refused, not how to forge one that passes: \"{refused.Message}\"");
    }

    [Fact]
    public void What_a_caller_gets_wrong_about_a_return_is_a_bad_request_with_its_own_code()
    {
        var refused = CourseVersionFailures.InvalidReturnReason();

        refused.Code.ShouldBe("editorial.invalid-return-reason");
        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Message.ShouldBe("A return's reason is 1 to 1000 characters after trimming, and holds no control or format characters but line breaks and tabs.");
    }

    [Fact]
    public void A_file_not_uploaded_yet_is_a_conflict_the_caller_may_retry()
    {
        CourseVersionFailures.UploadNotFound().Code.ShouldBe("editorial.upload-not-found");
        CourseVersionFailures.UploadNotFound().Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_caller_without_a_subject_is_forbidden()
    {
        CourseVersionFailures.IdentityIncomplete().Code.ShouldBe("editorial.identity-incomplete");
        CourseVersionFailures.IdentityIncomplete().Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>The token is incomplete, not the request wrong: forbidden, like a caller without a subject (#205).</summary>
    [Fact]
    public void A_caller_whose_token_names_no_reviewer_is_forbidden()
    {
        var refused = CourseVersionFailures.ReviewerNameMissing();

        refused.Code.ShouldBe("editorial.reviewer-name-missing");
        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        refused.Message.ShouldBe("The access token names no usable display name (claim `name`): it is missing, longer than 100 characters, or holds characters that cannot be shown.");
    }

    [Fact]
    public void A_version_that_does_not_exist_is_not_found()
    {
        var notFound = CourseVersionFailures.NotFound(Guid.CreateVersion7());

        notFound.Code.ShouldBe("editorial.course-version-not-found");
        notFound.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Changing_what_is_no_draft_is_a_conflict()
    {
        var error = new NotADraft(Version, CourseVersionStatus.Published);

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe("editorial.not-a-draft");
        failure.Status.ShouldBe(HttpStatusCode.Conflict);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    [Fact]
    public void A_chapter_the_version_does_not_hold_is_not_found()
    {
        var failure = CourseVersionFailures.From(new ChapterNotFound(Version, ChapterId.New()));

        failure.Code.ShouldBe("editorial.chapter-not-found");
        failure.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void An_order_that_does_not_match_the_chapters_is_a_conflict()
    {
        var failure = CourseVersionFailures.From(new OrderDoesNotMatchChapters(Version));

        failure.Code.ShouldBe("editorial.order-does-not-match");
        failure.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Any_other_rule_of_the_version_is_a_conflict_with_its_own_code()
    {
        var failure = CourseVersionFailures.From(new NoChapters(Version));

        failure.Code.ShouldBe("editorial.no-chapters");
        failure.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_question_the_version_does_not_hold_is_not_found()
    {
        var error = new QuestionNotFound(Version, QuestionId.New());

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe("editorial.question-not-found");
        failure.Status.ShouldBe(HttpStatusCode.NotFound);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    [Fact]
    public void A_question_beyond_the_maximum_is_a_conflict()
    {
        var error = new TooManyQuestions(Version);

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe("editorial.too-many-questions");
        failure.Status.ShouldBe(HttpStatusCode.Conflict);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    [Fact]
    public void A_blueprint_asking_for_too_few_questions_is_a_bad_request_with_the_question_count_code()
    {
        var error = new TooFewQuestions(QuestionCount.From(0));

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe("editorial.invalid-question-count");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    [Fact]
    public void A_question_count_out_of_range_reads_the_same_whether_the_endpoint_or_the_domain_refuses_it()
    {
        var fromEndpoint = CourseVersionFailures.InvalidQuestionCount();
        var fromDomain = CourseVersionErrors.TooFewQuestions(QuestionCount.From(0));

        fromEndpoint.Code.ShouldBe(fromDomain.Code);
        fromEndpoint.Message.ShouldBe(fromDomain.Message);
    }

    [Theory]
    [InlineData(nameof(TooFewOptions), "editorial.too-few-options")]
    [InlineData(nameof(SingleChoiceNeedsOneCorrect), "editorial.single-choice-one-correct")]
    [InlineData(nameof(MultipleChoiceNeedsACorrect), "editorial.multiple-choice-a-correct")]
    public void A_question_whose_content_breaks_a_rule_of_its_shape_cannot_be_processed(string rule, string code)
    {
        var error = Rule(rule);

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe(code);
        failure.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    [Theory]
    [InlineData(nameof(NotADraft), "editorial.not-a-draft")]
    [InlineData(nameof(NotInReview), "editorial.not-in-review")]
    [InlineData(nameof(NotPublished), "editorial.not-published")]
    [InlineData(nameof(OrderDoesNotMatchChapters), "editorial.order-does-not-match")]
    [InlineData(nameof(NoChapters), "editorial.no-chapters")]
    [InlineData(nameof(TestIncomplete), "editorial.test-incomplete")]
    [InlineData(nameof(QuestionNotApproved), "editorial.question-not-approved")]
    public void The_versions_state_rules_stay_a_conflict(string rule, string code)
    {
        var failure = CourseVersionFailures.From(Rule(rule));

        failure.Code.ShouldBe(code);
        failure.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>The version is in the right state, only this caller may not publish it: forbidden, with its own code.</summary>
    [Fact]
    public void Four_eyes_answers_forbidden()
    {
        var error = CourseVersionErrors.FourEyesRequired(Version);

        var failure = CourseVersionFailures.From(error);

        failure.Code.ShouldBe("editorial.four-eyes");
        failure.Status.ShouldBe(HttpStatusCode.Forbidden);
        failure.ShouldBeOfType<DomainFailure>().Error.ShouldBe(error);
    }

    private static DomainError Rule(string rule) => rule switch
    {
        nameof(NotADraft) => CourseVersionErrors.NotADraft(Version, CourseVersionStatus.Published),
        nameof(NotInReview) => CourseVersionErrors.NotInReview(Version, CourseVersionStatus.Draft),
        nameof(NotPublished) => CourseVersionErrors.NotPublished(Version, CourseVersionStatus.Draft),
        nameof(OrderDoesNotMatchChapters) => CourseVersionErrors.OrderDoesNotMatchChapters(Version),
        nameof(NoChapters) => CourseVersionErrors.NoChapters(Version),
        nameof(TestIncomplete) => CourseVersionErrors.TestIncomplete(Version, QuestionCount.From(10), QuestionCount.From(3)),
        nameof(QuestionNotApproved) => CourseVersionErrors.QuestionNotApproved(Version, QuestionId.New()),
        nameof(FourEyesRequired) => CourseVersionErrors.FourEyesRequired(Version),
        nameof(TooFewOptions) => CourseVersionErrors.TooFewOptions(QuestionId.New()),
        nameof(SingleChoiceNeedsOneCorrect) => CourseVersionErrors.SingleChoiceNeedsOneCorrect(QuestionId.New()),
        nameof(MultipleChoiceNeedsACorrect) => CourseVersionErrors.MultipleChoiceNeedsACorrect(QuestionId.New()),
        _ => throw new System.Diagnostics.UnreachableException($"No rule of the version is named {rule}."),
    };
}
