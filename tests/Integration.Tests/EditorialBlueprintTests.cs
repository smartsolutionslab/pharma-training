using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor sets the test blueprint of a draft through the edge (story E3): how many questions, the passing score
/// and how long a certificate is valid. A version is read back with its blueprint; what a caller gets wrong is
/// refused with its own code and never reaches 500; a version that is no draft keeps its blueprint.
/// </summary>
public class EditorialBlueprintTests(AspireFixture aspire)
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    [Fact]
    public async Task A_new_course_version_is_read_with_ten_questions_80_percent_and_12_months()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        var version = await ReadAsync(client, id, cancellationToken);

        version.Blueprint.ShouldBe(new BlueprintView(10, 80, 12));
    }

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<VersionView> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<VersionView>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private sealed record VersionView(Guid Id, string Status, BlueprintView? Blueprint);

    private sealed record BlueprintView(int Questions, int PassingScorePercent, int ValidityMonths);
}
