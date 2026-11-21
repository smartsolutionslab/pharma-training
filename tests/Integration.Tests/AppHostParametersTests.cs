using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The AppHost's model parameters, read from its application model, which is built and never started. A parameter
/// without a value makes Aspire's run mode hold back every resource that references it until someone sets it, so the
/// model's endpoint and key default to empty: the application starts without a model and says so when one is needed.
/// </summary>
public class AppHostParametersTests
{
    private const string Endpoint = "azure-openai-endpoint";

    private const string ApiKey = "azure-openai-api-key";

    private const string Deployment = "azure-openai-deployment";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task The_model_parameters_resolve_without_a_value_so_nothing_waits_for_them()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);

        var endpoint = await Parameter(builder, Endpoint).GetValueAsync(timeout.Token);
        var key = Parameter(builder, ApiKey);
        var keyValue = await key.GetValueAsync(timeout.Token);
        var deployment = await Parameter(builder, Deployment).GetValueAsync(timeout.Token);

        endpoint.ShouldBe(string.Empty);
        keyValue.ShouldBe(string.Empty);
        key.Secret.ShouldBeTrue();
        deployment.ShouldBe("gpt-5-mini");
    }

    [Fact]
    public async Task A_configured_model_parameter_wins_over_its_default()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);
        builder.Configuration[$"Parameters:{Endpoint}"] = "https://configured-resource.openai.azure.com";
        builder.Configuration[$"Parameters:{ApiKey}"] = "configured-key";

        var endpoint = await Parameter(builder, Endpoint).GetValueAsync(timeout.Token);
        var key = await Parameter(builder, ApiKey).GetValueAsync(timeout.Token);

        endpoint.ShouldBe("https://configured-resource.openai.azure.com");
        key.ShouldBe("configured-key");
    }

    /// <summary>A developer's user secrets must not decide what these tests see.</summary>
    private static void Unconfigured(IDistributedApplicationTestingBuilder builder)
    {
        builder.Configuration[$"Parameters:{Endpoint}"] = null;
        builder.Configuration[$"Parameters:{ApiKey}"] = null;
    }

    private static ParameterResource Parameter(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == name);
}
