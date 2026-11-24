using System.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The MigrationRunner as a service's init container runs it once published (docs/adr/0005): its own process, built in the
/// test's configuration, given the module, the administrator's connection string as <c>ConnectionStrings:database</c> and
/// the role's as <c>ConnectionStrings:application</c>, and only the runner settings a test names: no broker, no queue.
/// </summary>
public static class MigrationRunnerProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>Runs it to its end; answers its exit code and everything it printed.</summary>
    public static Task<(int ExitCode, string Output)> RunAsync(string module, string administrator, string application, CancellationToken cancellationToken) =>
        RunAsync(module, administrator, application, new Dictionary<string, string>(), cancellationToken);

    /// <summary>
    /// Runs it to its end with further runner settings, by their configuration key (<c>Migrations:IndexLockTimeout</c>),
    /// passed as an init container's environment; answers its exit code and everything it printed.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string module,
        string administrator,
        string application,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Assembly());
        start.Environment["Migrations__Module"] = module;
        start.Environment["ConnectionStrings__database"] = administrator;
        start.Environment["ConnectionStrings__application"] = application;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        foreach (var (key, value) in settings)
        {
            start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The MigrationRunner did not start.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The MigrationRunner did not finish within {Timeout}.");
        }

        return (process.ExitCode, $"{await output}\n{await error}");
    }

    /// <summary>The runner's assembly, built beside this one in the same configuration and target framework.</summary>
    private static string Assembly()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = output.Name;
        var configuration = output.Parent!.Name;
        var root = output;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.proj")))
        {
            root = root.Parent;
        }

        var assembly = Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory."),
            "src",
            "MigrationRunner",
            "bin",
            configuration,
            framework,
            "MigrationRunner.dll");
        return File.Exists(assembly)
            ? assembly
            : throw new InvalidOperationException($"{assembly} does not exist; build build.proj in {configuration} first.");
    }
}
