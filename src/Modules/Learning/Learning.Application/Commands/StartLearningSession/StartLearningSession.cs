using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;

/// <summary>Starts the learner's session on the course version, or resumes the one not finished yet (idempotent).</summary>
public sealed record StartLearningSession(PersonId Learner, CourseVersionId Version) : ICommand<Result<LearningSessionStart, ApiError>>;
