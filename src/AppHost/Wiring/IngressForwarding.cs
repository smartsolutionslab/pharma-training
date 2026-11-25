using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;

namespace Aspire.Hosting;

/// <summary>
/// Published, a project trusts exactly one <c>X-Forwarded-For</c> hop, the Container Apps ingress in front of it (#292,
/// docs/adr/0031): ASP.NET Core's forwarded headers, <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c>, take the client's
/// address from the entry the ingress appended, a forward limit of one from any proxy, and drop whatever the caller wrote
/// before it. The edge then forwards that address alone to the service, whose own ingress hop its forwarded headers take
/// off again, so a per-client limit counts the client. Aspire 13.6.5 sets the same on every project it publishes; this
/// names the trust here rather than leaving it to that default. In run mode nothing is trusted.
/// </summary>
public static class IngressForwarding
{
    private const string Variable = "ASPNETCORE_FORWARDEDHEADERS_ENABLED";

    public static IResourceBuilder<DotnetProjectResource> TrustsTheIngressHop(this IResourceBuilder<DotnetProjectResource> project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return project.ApplicationBuilder.ExecutionContext.IsPublishMode
            ? project.WithEnvironment(Variable, "true")
            : project;
    }
}
