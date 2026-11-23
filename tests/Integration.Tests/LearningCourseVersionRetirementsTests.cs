using System.Net;
using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's record of Editorial's retired course versions through the running application (#195): the retirement
/// reaches Learning through the broker and is stored in its own table, once, however often it arrives and whether or
/// not Learning holds the version yet; once Learning holds the version, its retirement refuses a new session on it.
/// </summary>
public class LearningCourseVersionRetirementsTests(AspireFixture aspire)
{
    private const string VersionsById = "select count(*) from learning.course_versions where id = @id";
    private const string RetirementsByVersion = "select count(*) from learning.course_version_retirements where course_version_id = @id";

    [Fact]
    public async Task A_retired_version_is_stored_as_one_retirement_with_its_course()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);

        await DeliverAsync(aspire, Retired(published), cancellationToken);

        (await RetirementsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem().ShouldBe(published.CourseId);
    }

    [Fact]
    public async Task A_retirement_delivered_twice_under_two_event_identifiers_is_stored_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);

        await DeliverAsync(aspire, Retired(published), cancellationToken);
        await DeliverAsync(aspire, Retired(published), cancellationToken);

        (await CountAsync(aspire, RetirementsByVersion, published.CourseVersionId, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_kept_and_the_version_is_still_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());

        await DeliverAsync(aspire, Retired(published), cancellationToken);
        await DeliverAsync(aspire, published, cancellationToken);

        (await CountAsync(aspire, RetirementsByVersion, published.CourseVersionId, cancellationToken)).ShouldBe(1);
        (await CountAsync(aspire, VersionsById, published.CourseVersionId, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_refuses_a_new_session_once_the_version_arrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(learner), cancellationToken);
        var published = Published(Guid.CreateVersion7());

        await DeliverAsync(aspire, Retired(published), cancellationToken);
        using var beforeTheVersion = await StartAsync(learner, published.CourseVersionId, cancellationToken);

        beforeTheVersion.StatusCode.ShouldBe(HttpStatusCode.NotFound, await beforeTheVersion.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(beforeTheVersion, cancellationToken)).ShouldBe("learning.course-version-not-found");

        await DeliverAsync(aspire, published, cancellationToken);
        await WaitUntilHeldAsync(aspire, published.CourseVersionId, cancellationToken);
        using var once = await StartAsync(learner, published.CourseVersionId, cancellationToken);

        once.StatusCode.ShouldBe(HttpStatusCode.Conflict, await once.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(once, cancellationToken)).ShouldBe("learning.course-version-retired");
    }

    /// <summary>The course of every retirement Learning stored for the version.</summary>
    private async Task<List<Guid>> RetirementsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select course_id from learning.course_version_retirements where course_version_id = @id", connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<Guid> courses = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            courses.Add(reader.GetGuid(0));
        }

        return courses;
    }
}
