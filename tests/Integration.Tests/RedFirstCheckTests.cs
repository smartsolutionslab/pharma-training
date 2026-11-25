using System.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// <c>.github/scripts/red-first.sh &lt;body&gt; &lt;base&gt; &lt;head&gt; &lt;author&gt;</c>, which the red-first workflow runs
/// on a pull request's merge ref (#429). The merge ref is the pull request merged into the base branch as it is when the
/// check runs, so the check diffs it from that base branch (<c>origin/main</c>): diffed from the event's older base, the
/// commits main gained since would count as the pull request's own, and their <c>src/</c> files would demand quoted tests.
/// Each test builds a small repository in a temporary directory.
/// </summary>
public class RedFirstCheckTests
{
    private static readonly string Scripts = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(Projects.AppHost.ProjectPath.EndsWith(".csproj", StringComparison.Ordinal) ? Path.GetDirectoryName(Projects.AppHost.ProjectPath)! : Projects.AppHost.ProjectPath)!,
        "..",
        ".github",
        "scripts"));

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_pull_request_touching_only_docs_passes_after_main_gained_a_source_commit()
    {
        var repository = await RepositoryAsync();
        try
        {
            var run = await RedFirstAsync(repository.Root, "origin/main", repository.Merge);

            run.ExitCode.ShouldBe(0, run.Output + run.Error);
            run.Output.ShouldContain("No source changed");
        }
        finally
        {
            Directory.Delete(repository.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Diffed_from_the_events_older_base_the_same_pull_request_would_be_refused()
    {
        var repository = await RepositoryAsync();
        try
        {
            var run = await RedFirstAsync(repository.Root, repository.EventBase, repository.Merge);

            run.ExitCode.ShouldBe(1, run.Output + run.Error);
            run.Output.ShouldContain("quotes no test output");
        }
        finally
        {
            Directory.Delete(repository.Root, recursive: true);
        }
    }

    [Fact]
    public async Task A_pull_request_that_changes_source_without_quoting_a_failing_test_is_still_refused()
    {
        var repository = await RepositoryAsync(pullRequestChangesSource: true);
        try
        {
            var run = await RedFirstAsync(repository.Root, "origin/main", repository.Merge);

            run.ExitCode.ShouldBe(1, run.Output + run.Error);
            run.Output.ShouldContain("quotes no test output");
        }
        finally
        {
            Directory.Delete(repository.Root, recursive: true);
        }
    }

    /// <summary>
    /// main at the event's base; a pull request branched from it; main then gains a commit under <c>src/</c>; and the merge
    /// ref, the pull request merged into main as it is now, which the workflow checks out.
    /// </summary>
    private static async Task<(string Root, string EventBase, string Merge)> RepositoryAsync(bool pullRequestChangesSource = false)
    {
        var root = Directory.CreateTempSubdirectory("red-first-").FullName;
        await GitAsync(root, "init", "--quiet", "--initial-branch=main");
        await GitAsync(root, "config", "user.email", "test@example.invalid");
        await GitAsync(root, "config", "user.name", "Red first test");
        await WriteAsync(root, "docs/readme.md", "docs\n");
        await WriteAsync(root, "src/Existing.cs", "// existing\n");
        await GitAsync(root, "add", "--all");
        await GitAsync(root, "commit", "--quiet", "--message", "chore: start");
        var eventBase = await GitAsync(root, "rev-parse", "HEAD");

        await GitAsync(root, "switch", "--quiet", "--create", "pull-request");
        await WriteAsync(root, pullRequestChangesSource ? "src/Changed.cs" : "docs/readme.md", "changed by the pull request\n");
        await GitAsync(root, "add", "--all");
        await GitAsync(root, "commit", "--quiet", "--message", "docs: change the readme");

        await GitAsync(root, "switch", "--quiet", "main");
        await WriteAsync(root, "src/MergedMeanwhile.cs", "// merged into main after the event\n");
        await GitAsync(root, "add", "--all");
        await GitAsync(root, "commit", "--quiet", "--message", "feat: merged meanwhile");
        await GitAsync(root, "update-ref", "refs/remotes/origin/main", "main");

        await GitAsync(root, "switch", "--quiet", "--detach", "main");
        await GitAsync(root, "merge", "--quiet", "--no-ff", "--message", "Merge pull-request into main", "pull-request");
        var merge = await GitAsync(root, "rev-parse", "HEAD");
        return (root, eventBase, merge);
    }

    private static async Task<CommandRun> RedFirstAsync(string root, string baseRef, string head)
    {
        var body = Path.Combine(root, ".git", "body.md");
        await File.WriteAllTextAsync(body, "## What changed and why\n\nNo test output quoted.\n");
        return await RunAsync(root, "bash", Path.Combine(Scripts, "red-first.sh"), body, baseRef, head, "someone");
    }

    private static async Task WriteAsync(string root, string path, string content)
    {
        var file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
    }

    private static async Task<string> GitAsync(string root, params string[] arguments)
    {
        var run = await RunAsync(root, "git", arguments);
        run.ExitCode.ShouldBe(0, $"git {string.Join(' ', arguments)}: {run.Error}");
        return run.Output.Trim();
    }

    private static async Task<CommandRun> RunAsync(string root, string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var limit = new CancellationTokenSource(Timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start.");
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);
        return new CommandRun(process.ExitCode, await output, await error);
    }
}
