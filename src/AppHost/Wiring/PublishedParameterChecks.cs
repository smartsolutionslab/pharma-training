using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

/// <summary>
/// Checks of a parameter's value that publishing (and so deploying) makes before it writes anything (docs/adr/0005): when
/// the publish starts (<see cref="BeforeStartEvent"/>), a value the deployment configured (<c>Parameters__&lt;name&gt;</c>)
/// that the check refuses fails the publish, naming the parameter. A parameter with no value yet is left alone: the Bicep
/// never holds its value, so <c>aspire publish --non-interactive</c> writes it without one, and waiting for it here would
/// stall the publish. Run mode checks nothing.
/// </summary>
public static class PublishedParameterChecks
{
    /// <summary>
    /// Refuses a value shorter than <paramref name="minimum"/> characters at publish, and declares the same floor on the
    /// parameter in main.bicep, <c>@minLength</c>, which Azure enforces however the value reaches the deployment
    /// (<see cref="MinimumLengthResolver"/>). A module that takes the parameter declares it on its own
    /// (<see cref="MinimumLengthParameter"/>). Length says nothing of a value's entropy: the deployment checks that.
    /// </summary>
    public static IResourceBuilder<ParameterResource> RequireMinimumLength(this IResourceBuilder<ParameterResource> parameter, int minimum)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);

        var builder = parameter.ApplicationBuilder;
        if (builder.ExecutionContext.IsPublishMode)
        {
            var bicepIdentifier = Infrastructure.NormalizeBicepIdentifier(parameter.Resource.Name);
            builder.Services.Configure<AzureProvisioningOptions>(options =>
                options.ProvisioningBuildOptions.InfrastructureResolvers.Add(new MinimumLengthResolver(bicepIdentifier, minimum)));
        }

        return parameter.CheckedWhenPublished(value => value.Length >= minimum
            ? null
            : $"must be at least {minimum} characters long; it has {value.Length}.");
    }

    /// <summary>
    /// Refuses a value that is no https origin: <c>https://host</c> or <c>https://host:port</c>, with no path, query,
    /// fragment, user, trailing slash or wildcard, and written as the canonical origin a browser sends: lower case, without
    /// the default port 443 and without a trailing dot on the host. Anything else would never match a browser's
    /// <c>Origin</c>, so CORS and the links in mail would fail.
    /// </summary>
    public static IResourceBuilder<ParameterResource> RequireHttpsOrigin(this IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return parameter.CheckedWhenPublished(value => IsHttpsOrigin(value)
            ? null
            : $"must be a canonical https origin, https://host or https://host:port in lower case, with no default port, path, query, trailing slash or dot, or wildcard; it is '{value}'.");
    }

    private static bool IsHttpsOrigin(string value) =>
        !value.Contains('*', StringComparison.Ordinal)
        && !value.EndsWith('/')
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0
        && uri.Host.Length > 0
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && !uri.Host.EndsWith('.')
        && string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);

    /// <summary>Fails publishing, naming the parameter, when <paramref name="problem"/> finds one with its value.</summary>
    private static IResourceBuilder<ParameterResource> CheckedWhenPublished(this IResourceBuilder<ParameterResource> parameter, Func<string, string?> problem)
    {
        var builder = parameter.ApplicationBuilder;
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return parameter;
        }

        var name = parameter.Resource.Name;
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            // Where Aspire reads the parameter's value from.
            if (builder.Configuration[$"Parameters:{name}"] is { } value && problem(value) is { } reason)
            {
                throw new InvalidOperationException($"The parameter '{name}' {reason}");
            }

            return Task.CompletedTask;
        });
        return parameter;
    }
}
