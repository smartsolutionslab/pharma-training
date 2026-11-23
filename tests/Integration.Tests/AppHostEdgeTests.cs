using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// E4 (#114 PR-D): published, the edge serves the web app. The web app is built once, as an image of its own from the pnpm
/// workspace (<c>web/Dockerfile</c>, context <c>web/</c>, so the lockfile is in it), which is never run: its build output is
/// copied into the edge's image under <c>./wwwroot</c>, and it is no Container App. Run mode keeps the dev server on 5173,
/// which OIDC redirect URIs name; the web app reads its identity provider from the edge at runtime (<c>/config.json</c>), so
/// nothing hands it one at build time.
/// </summary>
public partial class AppHostEdgeTests
{
    private const string Web = "web";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The repository's root, two folders above the AppHost's project folder (src/AppHost).</summary>
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        Projects.AppHost.ProjectPath.EndsWith(".csproj", StringComparison.Ordinal) ? Path.GetDirectoryName(Projects.AppHost.ProjectPath)! : Projects.AppHost.ProjectPath,
        "..",
        ".."));

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Published_the_edge_carries_the_web_build_in_wwwroot(string environment)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);

        var host = builder.Resources.Single(resource => resource.Name == "host");
        var copies = host.Annotations.OfType<ContainerFilesDestinationAnnotation>()
            .Select(copy => $"{copy.Source.Name} -> {copy.DestinationPath}")
            .ToList();
        copies.ShouldBe([$"{Web} -> ./wwwroot"], $"Published ({environment}), the edge's image should carry the web build in ./wwwroot (host.PublishWithContainerFiles(web, \"./wwwroot\")); it copies: [{string.Join(", ", copies)}]");
    }

    [Fact]
    public async Task Published_web_is_built_but_is_no_container_app()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var web = builder.Resources.SingleOrDefault(resource => resource.Name == Web);
        web.ShouldNotBeNull($"Published, there is no resource '{Web}': the web app is not built, so the edge has nothing to serve.");
        var build = web.Annotations.OfType<DockerfileBuildAnnotation>().ShouldHaveSingleItem($"Published, '{Web}' should be built from one Dockerfile.");
        build.DockerfilePath.ShouldBe(Path.Combine(RepositoryRoot, "web", "Dockerfile"), "The web image should be built from the project's own web/Dockerfile.");
        Path.TrimEndingDirectorySeparator(build.ContextPath).ShouldBe(Path.Combine(RepositoryRoot, "web"), "The web image's context should be the pnpm workspace, web/, which holds its lockfile.");
        build.HasEntrypoint.ShouldBeFalse($"'{Web}' is only built: an image with an entrypoint is published as a Container App.");

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        bicep.ContainerApps.Keys.ShouldNotContain(Web, $"'{Web}' is published as a Container App; it should only be built for the edge's image.");
        bicep.Files.Keys.Where(path => path.StartsWith($"{Web}/", StringComparison.Ordinal)).ShouldBeEmpty($"Publishing wrote a module for '{Web}'.");
    }

    [Fact]
    public async Task The_web_image_holds_the_workspace_build_where_the_edge_copies_it_from()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var dockerfilePath = Path.Combine(RepositoryRoot, "web", "Dockerfile");
        File.Exists(dockerfilePath).ShouldBeTrue("There is no web/Dockerfile.");
        var dockerfile = await File.ReadAllTextAsync(dockerfilePath, TestContext.Current.CancellationToken);
        var web = builder.Resources.SingleOrDefault(resource => resource.Name == Web);
        web.ShouldNotBeNull($"Published, there is no resource '{Web}'.");
        var source = web.Annotations.OfType<ContainerFilesSourceAnnotation>().ShouldHaveSingleItem($"'{Web}' should name the one folder of its image the edge copies.").SourcePath;

        dockerfile.ShouldContain("pnpm-lock.yaml", Case.Sensitive, "web/Dockerfile should copy the workspace's lockfile.");
        dockerfile.ShouldMatch(@"pnpm install --frozen-lockfile", "web/Dockerfile should install exactly what the lockfile names.");
        var stages = FromLine().Matches(dockerfile).Select(match => match.Groups["image"].Value).ToList();
        stages.LastOrDefault().ShouldBe("scratch", $"web/Dockerfile's last stage should hold the build output alone (FROM scratch); its stages: [{string.Join(", ", stages)}]");
        var lastStage = dockerfile[FromLine().Matches(dockerfile)[^1].Index..];
        CopyDestination().Matches(lastStage).Select(match => match.Groups["destination"].Value.TrimEnd('/')).ShouldContain(source.TrimEnd('/'), $"web/Dockerfile's last stage should COPY the build to {source}, where the edge copies it from.");
        dockerfile.ShouldNotContain("NODE_EXTRA_CA_CERTS", Case.Sensitive, "web/Dockerfile trusts an extra CA: that was the D0 spike's sandbox, not the project's build.");
        dockerfile.ShouldNotContain(".crt", Case.Sensitive, "web/Dockerfile copies a certificate: that was the D0 spike's sandbox, not the project's build.");

        var ignorePath = Path.Combine(RepositoryRoot, "web", ".dockerignore");
        File.Exists(ignorePath).ShouldBeTrue("There is no web/.dockerignore: node_modules would be sent as the web image's context.");
        var ignored = (await File.ReadAllLinesAsync(ignorePath, TestContext.Current.CancellationToken)).Select(line => line.Trim()).ToList();
        ignored.ShouldContain("**/node_modules", "web/.dockerignore should leave out every node_modules.");
        ignored.ShouldContain("**/dist", "web/.dockerignore should leave out every earlier build.");
    }

    /// <summary>
    /// The security review of #114 (PR-D): the web build runs in a base image a tag alone could move to one nobody reviewed,
    /// so every stage but the empty last one starts from its image by tag and digest, which Dependabot keeps current.
    /// </summary>
    [Fact]
    public async Task The_web_images_stages_start_from_a_digest()
    {
        var dockerfile = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "web", "Dockerfile"), TestContext.Current.CancellationToken);

        var stages = FromLine().Matches(dockerfile).Select(match => match.Groups["image"].Value).Where(image => image != "scratch").ToList();

        stages.ShouldNotBeEmpty("web/Dockerfile has no stage that starts from an image.");
        stages.Where(stage => !PinnedImage().IsMatch(stage)).ShouldBeEmpty("Every FROM of web/Dockerfile but FROM scratch should name its image as <image>:<tag>@sha256:<64 hex characters>; a tag alone can be moved.");
    }

    [Fact]
    public async Task Dependabot_keeps_the_web_images_base_current()
    {
        var dependabot = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, ".github", "dependabot.yml"), TestContext.Current.CancellationToken);

        DockerForWeb().IsMatch(dependabot).ShouldBeTrue(".github/dependabot.yml has no entry 'package-ecosystem: docker' with 'directory: /web': web/Dockerfile's base image would never be updated (AGENTS.md: a new ecosystem gets its own entry).");
    }

    [Fact]
    public async Task Run_mode_keeps_the_dev_server_on_5173()
    {
        await using var builder = await RunModeAsync();

        var web = builder.Resources.OfType<ExecutableResource>().SingleOrDefault(resource => resource.Name == Web);
        web.ShouldNotBeNull($"Run mode has no dev server '{Web}'.");
        Path.TrimEndingDirectorySeparator(web.WorkingDirectory).ShouldBe(Path.Combine(RepositoryRoot, "web", "app"), "In run mode the dev server runs in web/app.");
        web.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "http").Port.ShouldBe(5173, "OIDC redirect URIs name port 5173.");
        var waitsFor = web.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).ToList();
        waitsFor.ShouldContain("host", "The dev server proxies /api to the edge and waits for it.");
        waitsFor.ShouldContain("keycloak", "The dev server waits for the identity provider the app signs in with.");
        builder.Resources.Single(resource => resource.Name == "host").Annotations.OfType<ContainerFilesDestinationAnnotation>()
            .ShouldBeEmpty("In run mode the dev server serves the web app; the edge carries no build.");
    }

    [Fact]
    public async Task Run_mode_hands_the_web_app_no_identity_provider_at_build_time()
    {
        await using var builder = await RunModeAsync();
        var web = builder.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == Web);

        var context = new EnvironmentCallbackContext(builder.ExecutionContext, web, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in web.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        context.EnvironmentVariables.Keys.ShouldNotContain("VITE_OIDC_AUTHORITY", "The web app reads its identity provider from the edge's /config.json at runtime; VITE_OIDC_AUTHORITY should be gone from the AppHost.");
    }

    private static async Task<IDistributedApplicationTestingBuilder> RunModeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        return await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
    }

    [GeneratedRegex(@"^FROM\s+(?:--\S+\s+)*(?<image>\S+)", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FromLine();

    /// <summary>An image named by its tag and its digest: <c>name:tag@sha256:&lt;64 hex characters&gt;</c>.</summary>
    [GeneratedRegex(@"^[^\s@]+:[^\s@/]+@sha256:[0-9a-f]{64}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PinnedImage();

    [GeneratedRegex(@"^COPY\s+(?:--\S+\s+)*\S+\s+(?<destination>\S+)\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CopyDestination();

    [GeneratedRegex(@"-\s*package-ecosystem:\s*docker\s*\n\s*directory:\s*/web\s*\n", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DockerForWeb();
}
