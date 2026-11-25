using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// How the AppHost launches its projects, read from its application model, which is built and never started. Aspire 17
/// builds every <see cref="DotnetProjectResource"/> in one coordinated build before it starts them, the projects in a
/// group in parallel with one restore. A legacy <see cref="ProjectResource"/> (<c>AddProject</c>) is started with its own
/// <c>dotnet run</c> instead, which builds it again at every start unless the AppHost's build has built it.
/// </summary>
public class AppHostProjectLaunchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Every_project_the_AppHost_starts_is_built_in_Aspires_coordinated_build()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);

        var legacy = builder.Resources
            .OfType<ProjectResource>()
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        legacy.ShouldBeEmpty(
            "These projects are added with the obsolete AddProject, so each is run with its own dotnet run; "
            + "add them with AddDotnetProject(name, path), which Aspire builds in one coordinated build before it starts them.");
        builder.Resources.OfType<DotnetProjectResource>().ShouldNotBeEmpty();
    }
}
