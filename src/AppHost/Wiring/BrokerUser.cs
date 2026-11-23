using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>A service's own user on the broker, named after its module: its password, and what it consumes and publishes.</summary>
internal sealed record BrokerUser(
    string Module,
    IResourceBuilder<ParameterResource> Password,
    (IReadOnlyList<string> Consumed, IReadOnlyList<string> Published) Topology);
