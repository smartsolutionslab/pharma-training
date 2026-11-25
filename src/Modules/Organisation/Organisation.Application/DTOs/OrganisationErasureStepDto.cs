namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>One module's completed part of an erasure.</summary>
public sealed record OrganisationErasureStepDto(string Module, DateTimeOffset CompletedAt);
