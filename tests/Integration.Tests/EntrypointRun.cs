namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the broker's entrypoint did in a container: its exit code, what it printed, and, when it handed over to the image's
/// entrypoint, the arguments, the definitions file, the names of the variables that entrypoint was given, and one line per
/// file it looked at (its path, type, owner and group, mode and size), and last <c>umask NNNN</c>, the umask the image's
/// entrypoint, and so RabbitMQ, was handed.
/// </summary>
public sealed record EntrypointRun(int ExitCode, string Output, string Error, string Arguments, string Definitions, List<string> Variables, List<string> Files);
