using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Keycloak's published image (Docker required), built here by the AppHost's own Dockerfile from its own build context,
/// <c>src/AppHost/keycloak</c>, as <c>aspire deploy</c> builds it, and started in <c>start-dev</c> with a bootstrap
/// administrator, <c>admin</c>, whose password is made up for the run. Commands run in it with <c>docker exec</c>, as an
/// operator's do in <c>az containerapp exec</c>, from <c>/opt/keycloak/bin</c>.
/// </summary>
public sealed class KeycloakAdminContainer : IAsyncLifetime
{
    public const string Administrator = "admin";

    private const string Image = "pharma-training-keycloak-published:test";

    private const string BinDirectory = "/opt/keycloak/bin";

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private readonly string name = $"keycloak-admin-{Guid.CreateVersion7():N}";

    /// <summary>The bootstrap administrator's password, made up for this run.</summary>
    public string AdministratorPassword { get; } = Guid.CreateVersion7().ToString("N");

    public async ValueTask InitializeAsync()
    {
        var (dockerfile, context) = await PublishedDockerfileAsync();
        var file = Path.Combine(Path.GetTempPath(), $"{name}.Dockerfile");
        await File.WriteAllTextAsync(file, dockerfile);
        try
        {
            var build = await DockerAsync(["build", "-f", file, "-t", Image, context], new Dictionary<string, string>(StringComparer.Ordinal), null, BuildTimeout);
            build.ExitCode.ShouldBe(0, $"Keycloak's published image did not build:\n{build}");
        }
        finally
        {
            File.Delete(file);
        }

        var run = await DockerAsync(
            ["run", "-d", "--name", name, "-e", "KC_BOOTSTRAP_ADMIN_USERNAME=" + Administrator, "-e", "KC_BOOTSTRAP_ADMIN_PASSWORD", Image, "start-dev"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["KC_BOOTSTRAP_ADMIN_PASSWORD"] = AdministratorPassword },
            null,
            CommandTimeout);
        run.ExitCode.ShouldBe(0, $"Keycloak's published image did not start:\n{run}");

        var started = Stopwatch.StartNew();
        var signIn = await SignInAsAdministratorAsync();
        while (signIn.ExitCode != 0 && started.Elapsed < StartTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            signIn = await SignInAsAdministratorAsync();
        }

        signIn.ExitCode.ShouldBe(0, $"The bootstrap administrator could not sign in within {StartTimeout}:\n{signIn}");
    }

    public async ValueTask DisposeAsync() =>
        await DockerAsync(["rm", "-f", name], new Dictionary<string, string>(StringComparer.Ordinal), null, CommandTimeout);

    /// <summary>Signs kcadm.sh in as the bootstrap administrator, as the runbook's first step does.</summary>
    public Task<CommandRun> SignInAsAdministratorAsync() =>
        ExecAsync(
            $"./kcadm.sh config credentials --server http://localhost:8080 --realm master --user {Administrator}",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["KC_CLI_PASSWORD"] = AdministratorPassword });

    /// <summary>Creates an administrator of the master realm with a password, as the runbook does, signed in as <c>admin</c>.</summary>
    public async Task CreateAdministratorAsync(string user, string password)
    {
        (await SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
        var create = await ExecAsync(
            $"./kcadm.sh create users -r master -s username={user} -s enabled=true && ./kcadm.sh set-password -r master --username {user} --new-password \"$NEW_PASSWORD\" && ./kcadm.sh add-roles -r master --uusername {user} --rolename admin",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["NEW_PASSWORD"] = password });
        create.ExitCode.ShouldBe(0, create.ToString());
    }

    /// <summary>Runs a bash script in the container's <c>/opt/keycloak/bin</c>, with the given variables and nothing on standard input.</summary>
    public Task<CommandRun> ExecAsync(string script, IReadOnlyDictionary<string, string>? environment = null, string? input = null) =>
        DockerAsync(ExecArguments(script, environment), environment ?? new Dictionary<string, string>(StringComparer.Ordinal), null, CommandTimeout, input);

    /// <summary>
    /// Runs a bash script as <see cref="ExecAsync"/> does, answering it on standard input: each line it prints is shown to
    /// <paramref name="answer"/>, and the first answer that is not null is written, after which standard input is closed.
    /// </summary>
    public Task<CommandRun> ConverseAsync(string script, Func<string, string?> answer) =>
        DockerAsync(ExecArguments(script, null), new Dictionary<string, string>(StringComparer.Ordinal), answer, CommandTimeout);

    private List<string> ExecArguments(string script, IReadOnlyDictionary<string, string>? environment)
    {
        List<string> arguments = ["exec", "-i", "-w", BinDirectory];
        foreach (var variable in (environment ?? new Dictionary<string, string>(StringComparer.Ordinal)).Keys)
        {
            arguments.Add("-e");
            arguments.Add(variable);
        }

        arguments.AddRange([name, "bash", "-c", script]);
        return arguments;
    }

    /// <summary>
    /// Runs docker; variables are passed by name, so their values travel in docker's environment, not on its command line.
    /// Standard output is read line by line; the first non-null answer to a line is written to standard input, which is
    /// then closed. Without a responder standard input gets <paramref name="input"/>, or nothing, and is closed at once.
    /// </summary>
    private static async Task<CommandRun> DockerAsync(
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        Func<string, string?>? respond,
        TimeSpan timeout,
        string? input = null)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (variable, value) in environment)
        {
            start.Environment[variable] = value;
        }

        using var limit = new CancellationTokenSource(timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker did not start.");
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
        }

        var answered = false;
        if (respond is null || input is not null)
        {
            process.StandardInput.Close();
            answered = true;
        }

        var output = new System.Text.StringBuilder();
        while (await process.StandardOutput.ReadLineAsync(limit.Token) is { } line)
        {
            output.AppendLine(line);
            if (!answered && respond?.Invoke(line) is { } answer)
            {
                await process.StandardInput.WriteAsync(answer);
                process.StandardInput.Close();
                answered = true;
            }
        }

        if (!answered)
        {
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(limit.Token);
        return new CommandRun(process.ExitCode, output.ToString(), await error);
    }

    /// <summary>Keycloak's Dockerfile as the AppHost writes it for the staging publish, and the directory it is built from.</summary>
    private static async Task<(string Dockerfile, string Context)> PublishedDockerfileAsync()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, CancellationToken.None);
        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "keycloak");
        var build = keycloak.Annotations.OfType<DockerfileBuildAnnotation>().Single();
        build.DockerfileFactory.ShouldNotBeNull("Keycloak's Dockerfile should be written by the AppHost.");
        await using var app = await builder.BuildAsync(CancellationToken.None);
        var dockerfile = await build.DockerfileFactory(new DockerfileFactoryContext
        {
            Services = app.Services,
            Resource = keycloak,
            CancellationToken = CancellationToken.None,
        });
        return (dockerfile, build.ContextPath);
    }
}
