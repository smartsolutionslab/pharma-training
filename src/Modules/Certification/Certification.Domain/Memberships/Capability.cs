namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// What a member's role allows in Certification, asked of the caller's membership in the pharmacy (docs/adr/0003):
/// exporting the pharmacy's certificates (#52) and reading and setting how long the pharmacy keeps them (#391).
/// </summary>
public enum Capability
{
    ExportCertificates,

    ManageRetention,
}
