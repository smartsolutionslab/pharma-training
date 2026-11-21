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
    public void A_caller_without_a_subject_is_forbidden()
    {
        CourseVersionFailures.IdentityIncomplete().Code.ShouldBe("editorial.identity-incomplete");
        CourseVersionFailures.IdentityIncomplete().Status.ShouldBe(HttpStatusCode.Forbidden);
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
}
