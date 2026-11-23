using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The catalogue across Editorial's real publish of a next version (ADR 0007): Editorial supersedes the older version in
/// the same save and announces nothing, and Catalog reads the older version as superseded and lists only the newer.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class CatalogSupersededVersionsTests(AspireFixture aspire)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Publishing_the_next_version_in_editorial_makes_the_catalogue_read_the_older_as_superseded_and_list_only_the_newer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var reader = await aspire.CreateHostClientAsync("developer");
        var v1 = await PublishedAsync(editor, cancellationToken);
        (await WaitUntilReadableAsync(reader, v1.Id, cancellationToken)).Status.ShouldBe("Current");

        var v2 = await DraftNextAsync(editor, v1.Id, cancellationToken);
        await SubmitAsync(editor, v2, cancellationToken);
        await PublishAsync(editor, v2, cancellationToken);
        (await ReadAsync(editor, v1.Id, cancellationToken)).Status.ShouldBe("Superseded");
        var newer = await WaitUntilReadableAsync(reader, v2, cancellationToken);

        var older = await reader.GetFromJsonAsync<ReadVersion>(VersionUri(v1.Id), cancellationToken);
        older.ShouldNotBeNull();
        older.Status.ShouldBe("Superseded");
        older.IsRetired.ShouldBeFalse();
        newer.Status.ShouldBe("Current");
        var listed = (await ListedAsync(reader, v1.CourseId, cancellationToken)).ShouldHaveSingleItem();
        listed.CourseVersionId.ShouldBe(v2);
        listed.VersionNumber.ShouldBe(newer.VersionNumber);
    }

    private static Uri VersionUri(Guid id) => new($"/catalog/course-versions/{id}", UriKind.Relative);

    private static Uri CoursesUri(int page) => new($"/catalog/courses?page={page}&pageSize=100", UriKind.Relative);

    /// <summary>The version is stored once Catalog answers it.</summary>
    private static async Task<ReadVersion> WaitUntilReadableAsync(HttpClient client, Guid courseVersionId, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            using var response = await client.GetAsync(VersionUri(courseVersionId), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<ReadVersion>(cancellationToken))!;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"GET {VersionUri(courseVersionId)} still answered {(int)response.StatusCode} {response.StatusCode} after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Every entry of the course in the list, read a page at a time until the pages run out.</summary>
    private static async Task<List<ListedCourse>> ListedAsync(HttpClient client, Guid courseId, CancellationToken cancellationToken)
    {
        List<ListedCourse> found = [];
        for (var page = 1; ; page++)
        {
            var courses = await client.GetFromJsonAsync<CoursePage>(CoursesUri(page), cancellationToken);
            courses.ShouldNotBeNull();
            found.AddRange(courses.Items.Where(course => course.CourseId == courseId));
            if (courses.Items.Count == 0 || page * courses.PageSize >= courses.TotalCount)
            {
                return found;
            }
        }
    }

    private sealed record ReadVersion(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, bool IsRetired, string Status);

    private sealed record ListedCourse(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths);

    private sealed record CoursePage(IReadOnlyList<ListedCourse> Items, int TotalCount, int Page, int PageSize);
}
