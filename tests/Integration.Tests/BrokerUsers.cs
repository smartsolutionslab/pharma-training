using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The running application's broker users (#183): every service host's environment as the AppHost evaluates it, the
/// definitions the broker imports at boot (<c>BROKER_DEFINITIONS</c>), its management API under the admin user, and a
/// way to see whether the broker refuses what a connection of a service's own user tries.
/// </summary>
public static class BrokerUsers
{
    /// <summary>The broker's own resource, which Aspire names after the connection string the services read.</summary>
    public const string Broker = "messaging";

    /// <summary>The environment variable the broker's entrypoint writes its definitions from.</summary>
    public const string Definitions = "BROKER_DEFINITIONS";

    /// <summary>The broker's variable that holds the admin's password, as RabbitMQ's image reads it.</summary>
    public const string AdminPasswordVariable = "RABBITMQ_DEFAULT_PASS";

    /// <summary>The broker's variable that holds the admin's user name, as RabbitMQ's image reads it.</summary>
    public const string AdminUserVariable = "RABBITMQ_DEFAULT_USER";

    /// <summary>RabbitMQ's reply code for an operation the user's permissions refuse (ACCESS_REFUSED).</summary>
    public const ushort AccessRefused = 403;

    /// <summary>Every module's service host in the running application, found as the AppHost finds them.</summary>
    public static List<ProjectResource> ServiceHosts(AspireFixture aspire) =>
        [.. Model(aspire).Resources.OfType<ProjectResource>()
            .Where(project => IsServiceHost(project.GetProjectMetadata().ProjectPath))
            .OrderBy(project => project.Name, StringComparer.Ordinal)];

    /// <summary>The resource's environment with every value resolved, as the AppHost hands it to the process.</summary>
    public static async Task<Dictionary<string, string>> EnvironmentAsync(AspireFixture aspire, string resource, CancellationToken cancellationToken)
    {
        var found = Model(aspire).Resources.SingleOrDefault(candidate => candidate.Name == resource)
            ?? throw new ShouldAssertException($"The application has no resource '{resource}'.");
        var context = aspire.App.Services.GetRequiredService<DistributedApplicationExecutionContext>();
        var configuration = await ExecutionConfigurationBuilder.Create(found)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(context, NullLogger.Instance, cancellationToken);
        return configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
    }

    /// <summary>The connection string a service reads as <c>messaging</c>.</summary>
    public static async Task<string> ConnectionStringOfAsync(AspireFixture aspire, string service, CancellationToken cancellationToken)
    {
        var environment = await EnvironmentAsync(aspire, service, cancellationToken);
        environment.ShouldContainKey($"ConnectionStrings__{Broker}", $"{service} reads no broker connection string.");
        return environment[$"ConnectionStrings__{Broker}"];
    }

    /// <summary>
    /// The broker's variable that holds a service user's password, <c>BROKER_PASSWORD_&lt;MODULE&gt;</c>: the module in
    /// upper case, a character a shell variable's name may not hold as <c>_</c>.
    /// </summary>
    public static string PasswordVariable(string module) =>
        "BROKER_PASSWORD_" + string.Concat(module.ToUpperInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));

    /// <summary>
    /// The broker's variable that holds a service user's name, <c>BROKER_USER_&lt;MODULE&gt;</c>, spelled as its password's.
    /// </summary>
    public static string UserVariable(string module) =>
        "BROKER_USER_" + PasswordVariable(module)["BROKER_PASSWORD_".Length..];

    /// <summary>The text that stands for a variable's value in the definitions until the broker's entrypoint fills it in.</summary>
    public static string Placeholder(string variable) => $"@@{variable}@@";

    /// <summary>The user name in an AMQP connection string.</summary>
    public static string UserOf(string connectionString) =>
        Uri.UnescapeDataString(new Uri(connectionString).UserInfo.Split(':', 2)[0]);

    /// <summary>The password in an AMQP connection string.</summary>
    public static string PasswordOf(string connectionString) =>
        Uri.UnescapeDataString(new Uri(connectionString).UserInfo.Split(':', 2)[1]);

    /// <summary>The broker's admin connection string, which the tests keep and no service may hold.</summary>
    public static async Task<string> AdminConnectionStringAsync(AspireFixture aspire, CancellationToken cancellationToken) =>
        await aspire.App.GetConnectionStringAsync(Broker, cancellationToken)
            ?? throw new InvalidOperationException("The broker has no connection string.");

    /// <summary>
    /// The definitions the broker imports at every boot, from its environment, with each user name's placeholder filled in
    /// from the broker's variable as its entrypoint does; every password stays the placeholder it is.
    /// </summary>
    public static async Task<JsonDocument> DefinitionsAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        var environment = await EnvironmentAsync(aspire, Broker, cancellationToken);
        environment.ShouldContainKey(Definitions, $"The broker is given no {Definitions}; it creates no user of a service's own.");
        var definitions = environment[Definitions];
        foreach (var (variable, value) in environment.Where(variable => variable.Key == AdminUserVariable || variable.Key.StartsWith("BROKER_USER_", StringComparison.Ordinal)))
        {
            definitions = definitions.Replace(Placeholder(variable), value, StringComparison.Ordinal);
        }

        return JsonDocument.Parse(definitions);
    }

    /// <summary>A connection of the given user's own, as a service holding that connection string makes it.</summary>
    public static async Task<IConnection> ConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString), ClientProvidedName = "broker-permissions-tests" };
        return await factory.CreateConnectionAsync(cancellationToken);
    }

    /// <summary>The management API of the application's broker, signed in as its admin user.</summary>
    public static async Task<HttpClient> ManagementClientAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        var userInfo = new Uri(await AdminConnectionStringAsync(aspire, cancellationToken)).UserInfo.Split(':', 2);
        var credentials = $"{Uri.UnescapeDataString(userInfo[0])}:{Uri.UnescapeDataString(userInfo[1])}";
        var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint(Broker, "management") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        return client;
    }

    /// <summary>
    /// Runs the attempt on a channel of its own, then asks the broker a passive question on the same channel, which nothing
    /// refuses, so an asynchronous refusal (a publish) has arrived by then. Answers why the broker closed the channel, or
    /// null when it allowed the attempt.
    /// </summary>
    public static async Task<ShutdownEventArgs?> RefusalOfAsync(IConnection connection, Func<IChannel, Task> attempt, CancellationToken cancellationToken)
    {
        Ensure.That(connection).IsNotNull();
        Ensure.That(attempt).IsNotNull();

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            await attempt(channel);
            await channel.ExchangeDeclarePassiveAsync("amq.fanout", cancellationToken);
            return null;
        }
        catch (OperationInterruptedException refused)
        {
            return refused.ShutdownReason ?? channel.CloseReason;
        }
    }

    /// <summary>The alternatives of a permission pattern written <c>^(a|b|c)$</c>, unescaped; it fails on any other shape.</summary>
    public static List<string> Alternatives(string pattern)
    {
        pattern.ShouldStartWith("^(", Case.Sensitive, $"The permission '{pattern}' is not anchored at its start as ^(...)$.");
        pattern.ShouldEndWith(")$", Case.Sensitive, $"The permission '{pattern}' is not anchored at its end as ^(...)$.");
        return [.. pattern[2..^2].Split('|').Select(System.Text.RegularExpressions.Regex.Unescape).Order(StringComparer.Ordinal)];
    }

    private static DistributedApplicationModel Model(AspireFixture aspire) =>
        aspire.App.Services.GetRequiredService<DistributedApplicationModel>();

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
