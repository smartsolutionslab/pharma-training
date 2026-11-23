using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Republishing Editorial's course-version events (#189) requires editorial.edit, the policy of every other
/// course-version route: an editor is answered the counts of what went out again, and a user who holds only the
/// sample role manager, which editorial.manage requires, is forbidden. The pharmacy owner and the anonymous caller are
/// rows of <see cref="EditorialCourseVersionRoleTests"/>.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialRepublishRoleTests(AspireFixture aspire)
{
    private static readonly Uri Republish = new("/editorial/course-versions/republish", UriKind.Relative);

    [Fact]
    public async Task An_editor_republishes_and_is_answered_200_with_the_counts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var response = await client.PostAsync(Republish, null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {Republish} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        var counts = await response.Content.ReadFromJsonAsync<RepublishedCounts>(cancellationToken);
        counts.ShouldNotBeNull();
        counts.CourseVersions.ShouldBeGreaterThanOrEqualTo(0);
        counts.TestDefinitions.ShouldBeGreaterThanOrEqualTo(0);
        counts.Retirements.ShouldBeGreaterThanOrEqualTo(0);
        counts.TestDefinitions.ShouldBe(counts.CourseVersions);
    }

    /// <summary>editorial.manage is the module's sample policy; the role it asks for grants no editorial operation.</summary>
    [Fact]
    public async Task A_developer_holding_only_the_sample_manager_role_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");

        using var response = await client.PostAsync(Republish, null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>The answer as the test reads it; the module's DTO is not referenced.</summary>
    private sealed record RepublishedCounts(int CourseVersions, int TestDefinitions, int Retirements);
}
