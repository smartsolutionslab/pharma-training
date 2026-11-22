using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// How the AppHost launches its projects, read from its application model, which is built and never started. A project
/// resource whose metadata does not suppress the build is started with <c>dotnet run</c> without <c>--no-build</c>, so
/// it is built again at every start although the AppHost's own build has already built it.
/// </summary>
public class AppHostProjectLaunchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Every_project_the_AppHost_starts_is_one_its_build_has_built()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);

        var rebuilt = builder.Resources
            .OfType<ProjectResource>()
            .Where(project => !project.GetProjectMetadata().SuppressBuild)
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        rebuilt.ShouldBeEmpty(
            "These projects are rebuilt by `dotnet run` at every start, on top of the AppHost's own build; "
            + "add them from the generated Projects.* metadata, which suppresses the build.");
    }
}
