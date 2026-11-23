namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the broker's entrypoint did in a container: its exit code, what it printed, and, when it handed over to the image's
/// entrypoint, the arguments, the definitions file, the names of the variables that entrypoint was given, and the owner,
/// size and content of a file a planted symlink pointed to (empty without one).
/// </summary>
public sealed record EntrypointRun(int ExitCode, string Output, string Error, string Arguments, string Definitions, List<string> Variables, string Planted);
