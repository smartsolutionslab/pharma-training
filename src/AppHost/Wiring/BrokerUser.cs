using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>A service's own user on the broker: its module, user name and password, and what it consumes and publishes.</summary>
internal sealed record BrokerUser(
    string Module,
    IResourceBuilder<ParameterResource> UserName,
    IResourceBuilder<ParameterResource> Password,
    (IReadOnlyList<string> Consumed, IReadOnlyList<string> Published) Topology);
