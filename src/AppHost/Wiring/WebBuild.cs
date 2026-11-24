using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Published, the edge serves the web app (docs/adr/0005): the app is built once, as an image of its own from the pnpm
/// workspace (web/Dockerfile, context web/, so its lockfile is in it), whose build output Aspire copies into the edge's
/// image under ./wwwroot. That image is only built, never run: without an entrypoint it is no Container App. In run mode
/// nothing is added; the dev server serves the app.
/// </summary>
public static class WebBuild
{
    private const string Name = "web";

    /// <summary>The pnpm workspace, relative to the AppHost; its Dockerfile is the one Aspire builds.</summary>
    private const string Workspace = "../../web";

    private const string WebRoot = "./wwwroot";

    public static IResourceBuilder<ProjectResource> PublishWithTheWebBuild(this IResourceBuilder<ProjectResource> edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var builder = edge.ApplicationBuilder;
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return edge;
        }

        var web = builder.AddViteApp(Name, Workspace).WithPnpm();
        foreach (var build in web.Resource.Annotations.OfType<DockerfileBuildAnnotation>())
        {
            // web/Dockerfile ends in a stage that holds the build output alone; there is nothing to start.
            build.HasEntrypoint = false;
        }

        return edge.PublishWithContainerFiles(web, WebRoot);
    }
}
