namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>A read URL on a certificate's PDF, served inline, and when it stops working, so the page asks again in time.</summary>
public sealed record CertificationDocumentUrlDto(Uri Url, DateTimeOffset ExpiresAt);
