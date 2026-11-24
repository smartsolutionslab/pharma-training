using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Primitives;

namespace Aspire.Hosting;

/// <summary>
/// Declares <c>@minLength(n)</c> on one of main.bicep's parameters (<see cref="MinimumLengthParameter"/>). Aspire writes
/// main.bicep's parameters itself, one plain parameter per Aspire parameter, and offers no hook into them but the build
/// options' infrastructure resolvers (<see cref="AzureProvisioningOptions.ProvisioningBuildOptions"/>), which it builds
/// main.bicep with: this one swaps the plain parameter for one that declares the floor, keeping its name, its
/// <c>@secure()</c> and its description. A module's own parameters are its own (<see cref="AzureResourceInfrastructure"/>)
/// and left alone.
/// </summary>
public sealed class MinimumLengthResolver : InfrastructureResolver
{
    private readonly string bicepIdentifier;

    private readonly int minimum;

    public MinimumLengthResolver(string bicepIdentifier, int minimum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bicepIdentifier);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);

        this.bicepIdentifier = bicepIdentifier;
        this.minimum = minimum;
    }

    public override void ResolveInfrastructure(Infrastructure infrastructure, ProvisioningBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        base.ResolveInfrastructure(infrastructure, options);
        if (infrastructure is AzureResourceInfrastructure)
        {
            return;
        }

        var plain = infrastructure.GetProvisionableResources()
            .OfType<ProvisioningParameter>()
            .SingleOrDefault(parameter => parameter is not MinimumLengthParameter && parameter.BicepIdentifier == bicepIdentifier);
        if (plain is null)
        {
            return;
        }

        infrastructure.Remove(plain);
        infrastructure.Add(new MinimumLengthParameter(bicepIdentifier, minimum) { IsSecure = plain.IsSecure, Description = plain.Description });
    }
}
