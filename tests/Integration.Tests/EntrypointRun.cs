namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the broker's entrypoint did in a container: its exit code, what it printed, and, when it handed over to the image's
/// entrypoint, the arguments, the definitions file and the names of the variables that entrypoint was given.
/// </summary>
public sealed record EntrypointRun(int ExitCode, string Output, string Error, string Arguments, string Definitions, List<string> Variables);
