namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>The body of <c>PUT /certification/pharmacies/{id}/retention-period</c>: whole years from 2 to 30 (#391).</summary>
public sealed record SetCertificationRetentionPeriodRequest(int? Years);
