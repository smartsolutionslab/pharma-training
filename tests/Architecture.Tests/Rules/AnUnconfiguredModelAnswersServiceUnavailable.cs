using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// A service host started without the model's endpoint and key, as the AppHost runs it for a developer who set no
/// key, still starts; a request that needs the model then answers <c>503</c> problem details with the code
/// <c>model-not-configured</c>, not a bare <c>500</c>. Each host is started the way
/// <see cref="EveryEndpointDeclaresItsAuthorization"/> starts it, with no <c>AzureOpenAI:*</c> settings; resolving
/// <see cref="IChatClient"/> throws, and that exception is passed through the host's own exception handlers.
/// </summary>
public sealed class AnUnconfiguredModelAnswersServiceUnavailable : IArchitectureRule
{
    private const string Code = "model-not-configured";

    public string Description => "Every service host answers a request that needs an unconfigured model with 503 problem details, code model-not-configured.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. ServiceHostEntryPoints.All.SelectMany(Violations)];
    }

    private static List<string> Violations(Type entryPoint)
    {
        var name = entryPoint.Assembly.GetName().Name;
        using var host = (IUnconfiguredHost)Activator.CreateInstance(typeof(UnconfiguredHost<>).MakeGenericType(entryPoint))!;
        using var scope = host.Services.CreateScope();

        Exception thrown;
        try
        {
            scope.ServiceProvider.GetRequiredService<IChatClient>();
            return [$"{name} resolves an IChatClient without AzureOpenAI:Endpoint and AzureOpenAI:ApiKey; an unconfigured model must fail when it is needed."];
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        var (handled, status, code) = Answer(scope.ServiceProvider, thrown).GetAwaiter().GetResult();
        if (!handled)
        {
            return [$"In {name} no exception handler answered a missing model ({thrown.GetType().Name}); the host answers 500."];
        }

        List<string> violations = [];
        if (status != StatusCodes.Status503ServiceUnavailable)
        {
            violations.Add($"{name} answers a missing model with {status}, not 503.");
        }

        if (code != Code)
        {
            violations.Add($"{name} answers a missing model with the problem code '{code}', not '{Code}'.");
        }

        return violations;
    }

    private static async Task<(bool Handled, int Status, string? Code)> Answer(IServiceProvider services, Exception exception)
    {
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Accept = "application/json";
        context.Response.Body = body;

        foreach (var handler in services.GetServices<IExceptionHandler>())
        {
            if (await handler.TryHandleAsync(context, exception, CancellationToken.None))
            {
                return (true, context.Response.StatusCode, ProblemCode(body));
            }
        }

        return (false, context.Response.StatusCode, null);
    }

    private static string? ProblemCode(MemoryStream body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body.ToArray());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private interface IUnconfiguredHost : IDisposable
    {
        IServiceProvider Services { get; }
    }

    /// <summary>The host with nothing started and no model configured: neither endpoint, nor deployment, nor key.</summary>
    private sealed class UnconfiguredHost<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IUnconfiguredHost
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:database", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:messaging", "amqp://localhost");
            builder.UseSetting("Messaging:Queue", "unused");
            builder.UseSetting("Authentication:Authority", "http://localhost/realms/unused");
            builder.UseSetting("AzureOpenAI:Endpoint", string.Empty);
            builder.UseSetting("AzureOpenAI:Deployment", string.Empty);
            builder.UseSetting("AzureOpenAI:ApiKey", string.Empty);
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
