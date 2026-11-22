namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>What the test of a course version must look like: how many questions, the passing score and how long a certificate is valid.</summary>
public sealed record EditorialTestBlueprintDto(int Questions, int PassingScorePercent, int ValidityMonths);
