namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>A command's exit code and what it printed to standard output and standard error.</summary>
public sealed record CommandRun(int ExitCode, string Output, string Error)
{
    /// <summary>Both streams, for a failure message.</summary>
    public override string ToString() => $"exit {ExitCode}\n--- stdout ---\n{Output}\n--- stderr ---\n{Error}";
}
