using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Runs the broker's entrypoint script, as the AppHost publishes it, in the broker's own image (Docker required). The
/// image's <c>docker-entrypoint.sh</c> is replaced by a stub on the <c>PATH</c> that prints its arguments, the definitions
/// file, the names of the variables it was handed and the type, owner, group, mode and size of the two files and of what a
/// run planted (<c>PLANT_LINKS</c>: <c>files</c> links each file to a target of its own, <c>conf.d</c> and <c>etc</c> link
/// the directory to another), as <c>env</c> lists them (bash's own <c>compgen -e</c> leaves out a
/// name that is not a shell identifier, which still reaches the process), and starts no RabbitMQ.
/// </summary>
public static class BrokerEntrypoint
{
    private const string Separator = "----8<----";

    /// <summary>Takes the place of the image's entrypoint; it prints what the broker's entrypoint handed over.</summary>
    private const string Stub = $$"""
        mkdir -p /stub
        cat > /stub/docker-entrypoint.sh <<'STUB'
        #!/bin/bash
        printf '%s\n' "$*"
        printf '%s\n' '{{Separator}}'
        cat /tmp/rabbitmq-definitions.json
        printf '\n%s\n' '{{Separator}}'
        env | cut -d= -f1
        printf '%s\n' '{{Separator}}'
        for file in /tmp/rabbitmq-definitions.json /etc/rabbitmq/conf.d/90-definitions.conf /tmp/planted-definitions /tmp/planted-config; do
          if [ -e "$file" ] || [ -L "$file" ]; then printf '%s %s\n' "$file" "$(stat -c '%F %U:%G %a %s' "$file")"; fi
        done
        STUB
        chmod +x /stub/docker-entrypoint.sh
        case "${PLANT_LINKS-}" in
          files)
            printf 'planted' > /tmp/planted-definitions
            printf 'planted' > /tmp/planted-config
            ln -sf /tmp/planted-definitions /tmp/rabbitmq-definitions.json
            ln -sf /tmp/planted-config /etc/rabbitmq/conf.d/90-definitions.conf;;
          conf.d)
            mkdir -p /tmp/elsewhere
            rm -rf /etc/rabbitmq/conf.d
            ln -s /tmp/elsewhere /etc/rabbitmq/conf.d;;
          etc)
            mv /etc/rabbitmq /tmp/etc-rabbitmq
            ln -s /tmp/etc-rabbitmq /etc/rabbitmq;;
        esac
        export PATH="/stub:$PATH"
        exec /bin/bash -c "$1"
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>The broker's image and its entrypoint script, from the staging publish model.</summary>
    public static async Task<(string Image, string Script)> PublishedAsync(CancellationToken cancellationToken)
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, cancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();
        var image = broker.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();

        List<object> arguments = [];
        var context = new CommandLineArgsCallbackContext(arguments, broker, cancellationToken);
        foreach (var callback in broker.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        arguments.Count.ShouldBe(2, $"The broker's arguments are {string.Join(" ", arguments)}");
        var reference = $"{(image.Registry is null ? string.Empty : image.Registry + "/")}{image.Image}:{image.Tag}";
        return (reference, arguments[1].ShouldBeOfType<string>());
    }

    /// <summary>Runs the script as the broker's entrypoint with exactly the given variables of the broker's own.</summary>
    public static async Task<EntrypointRun> RunAsync(string image, string script, Dictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "run", "--rm", "--entrypoint", "/bin/bash" })
        {
            start.ArgumentList.Add(argument);
        }

        // Passed by name, so the values travel in docker's environment, not on its command line.
        foreach (var (name, value) in environment)
        {
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(name);
            start.Environment[name] = value;
        }

        foreach (var argument in new[] { image, "-c", Stub, "entrypoint", script })
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker did not start.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        var parts = (await output).Split(Separator + "\n");
        return parts.Length == 4
            ? new EntrypointRun(process.ExitCode, await output, await error, parts[0].TrimEnd('\n'), parts[1].TrimEnd('\n'), [.. parts[2].Split('\n', StringSplitOptions.RemoveEmptyEntries)], [.. parts[3].Split('\n', StringSplitOptions.RemoveEmptyEntries)])
            : new EntrypointRun(process.ExitCode, await output, await error, string.Empty, string.Empty, [], []);
    }
}
