namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The test classes that call <c>GET /certification/verify/{code}</c>: every local caller reaches Certification from
/// the edge's one address, so they share one per-address count (docs/adr/0026) and run one after the other, and no
/// other test calls the endpoint.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CertificationVerification
{
    public const string Name = "Certification verification";
}
