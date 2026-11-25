using Aspire.Hosting.Dotnet;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The AppHost as <c>aspire publish -e &lt;environment&gt;</c> sees it, in this process and without Docker: its publish-mode
/// application model, and the Bicep its publish pipeline writes. Aspire 13.6.5 takes the CLI's own arguments here
/// (<c>--operation publish --step publish --environment --output-path</c>); the model builds in well under a second, and
/// the pipeline writes the Bicep in about one more.
/// </summary>
public static class PublishedAppHost
{
    /// <summary>The environments the deploy workflow publishes.</summary>
    public const string Staging = "staging";

    public const string Production = "production";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    // A parameter without a value makes the pipeline wait for someone to enter it, without a word and without end; every
    // parameter gets a placeholder below, and this names the hang should one slip through.
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(2);

    // The shortest placeholder: database-roles-key must be at least 32 characters (docs/adr/0005).
    private const int PlaceholderLength = 32;

    // Parameters whose placeholder must have a form of its own: frontend-url is an https origin.
    private static readonly Dictionary<string, string> FormedPlaceholders = new(StringComparer.Ordinal)
    {
        ["frontend-url"] = "https://placeholder.example",
    };

    /// <summary>The publish-mode model for an environment, built and never started. Dispose it.</summary>
    public static async Task<IDistributedApplicationTestingBuilder> ModelAsync(string environment, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModelTimeout);
        return await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(Arguments(environment, OutputDirectory()), timeout.Token);
    }

    /// <summary>
    /// Runs the publish pipeline for an environment into a fresh directory and returns every file it wrote; the directory
    /// is removed again. A pipeline that fails throws its own exception here.
    /// </summary>
    public static Task<PublishedBicep> PublishAsync(string environment, CancellationToken cancellationToken) =>
        PublishAsync(environment, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken);

    /// <summary>As <see cref="PublishAsync(string, CancellationToken)"/>, with the given values for some parameters, by name.</summary>
    public static async Task<PublishedBicep> PublishAsync(string environment, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var output = OutputDirectory();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PublishTimeout);
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(Arguments(environment, output), timeout.Token);
            foreach (var (name, value) in parameters)
            {
                builder.Configuration[$"Parameters:{name}"] = value;
            }

            Placeholders(builder);

            await using var app = await builder.BuildAsync(timeout.Token);
            try
            {
                await app.StartAsync(timeout.Token);
                await app.WaitForShutdownAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Publishing '{environment}' did not finish within {PublishTimeout}; a parameter without a value makes the pipeline wait for input.");
            }

            return PublishedBicep.Read(output);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>Every module's service host, found as the AppHost finds them: src/Services/*/Host/Host.csproj.</summary>
    public static List<DotnetProjectResource> Services(IDistributedApplicationTestingBuilder builder) =>
        [.. builder.Resources.OfType<DotnetProjectResource>()
            .Where(project => IsServiceHost(project.GetProjectMetadata().ProjectPath))
            .OrderBy(project => project.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The CLI's publish arguments. <c>--clear-cache</c> keeps the run out of the deployment state Aspire otherwise reads
    /// and writes under ~/.aspire/deployments (generated passwords included); an existing state there is left alone.
    /// </summary>
    private static string[] Arguments(string environment, string output) =>
        ["--operation", "publish", "--step", "publish", "--environment", environment, "--output-path", output, "--clear-cache", "true"];

    private static string OutputDirectory() =>
        Path.Combine(Path.GetTempPath(), "pharma-training-publish", Guid.CreateVersion7().ToString("N"));

    /// <summary>
    /// A value for every parameter that has neither a default nor a configured one, so the pipeline never waits for it, in
    /// the form the AppHost accepts: at least 32 characters, an https origin for frontend-url. The Bicep declares such a
    /// parameter and never holds its value, so the placeholder changes nothing it writes.
    /// </summary>
    private static void Placeholders(IDistributedApplicationTestingBuilder builder)
    {
        foreach (var parameter in builder.Resources.OfType<ParameterResource>().Where(parameter => parameter.Default is null))
        {
            var key = $"Parameters:{parameter.Name}";
            builder.Configuration[key] ??= FormedPlaceholders.GetValueOrDefault(parameter.Name) ?? $"placeholder-{parameter.Name}".PadRight(PlaceholderLength, '-');
        }
    }

    private static bool IsServiceHost(string projectPath)
    {
        var host = Path.GetDirectoryName(projectPath);
        var service = Path.GetDirectoryName(host);
        var services = Path.GetDirectoryName(service);
        return Path.GetFileName(projectPath) == "Host.csproj"
            && Path.GetFileName(host) == "Host"
            && Path.GetFileName(services) == "Services";
    }
}
