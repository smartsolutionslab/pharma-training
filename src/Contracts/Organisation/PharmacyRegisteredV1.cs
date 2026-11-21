namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>Organisation registered a pharmacy; consumed by Compliance and Certification.</summary>
public sealed record PharmacyRegisteredV1(Guid PharmacyId, string Name) : IntegrationEvent;
