using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;

/// <summary>The learner reports how far they got in one chapter of their own session; only real progress is stored.</summary>
public sealed record ReportProgress(LearningSessionId Session, PersonId Learner, ChapterId Chapter, ProgressPercent Progress) : ICommand<Result<Unit, ApiError>>;
