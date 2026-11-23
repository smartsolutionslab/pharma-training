namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

/// <summary>What one assignment request did: each learner assigned, and each refused with the reason.</summary>
public sealed record AssignmentBatch(IReadOnlyList<LearnerAssigned> Assigned, IReadOnlyList<LearnerRefused> Refused);
