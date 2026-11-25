using System.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The demo seeder (#457) as its own process, built beside this assembly in the same configuration, given only the
/// <c>DemoSeeder:*</c> settings a test names, as the AppHost passes them in run mode: no telemetry endpoint.
/// </summary>
public static class DemoSeederProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    /// <summary>Runs it to its end; answers its exit code and everything it printed.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Assembly()),
        };
        start.ArgumentList.Add(Assembly());
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        foreach (var (key, value) in settings)
        {
            start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The demo seeder did not start.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The demo seeder did not finish within {Timeout}.");
        }

        return (process.ExitCode, $"{await output}\n{await error}");
    }

    /// <summary>The seeder's assembly, built beside this one in the same configuration and target framework.</summary>
    private static string Assembly()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = output.Name;
        var configuration = output.Parent?.Name ?? throw new InvalidOperationException($"{output} has no parent.");
        var root = output;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.proj")))
        {
            root = root.Parent;
        }

        var assembly = Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory."),
            "src",
            "DemoSeeder",
            "bin",
            configuration,
            framework,
            "DemoSeeder.dll");
        return File.Exists(assembly)
            ? assembly
            : throw new InvalidOperationException($"{assembly} does not exist; build build.proj in {configuration} first.");
    }
}
