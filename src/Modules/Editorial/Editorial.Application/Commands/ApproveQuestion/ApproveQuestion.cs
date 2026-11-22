using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;

/// <summary>An editor signs a question of a draft or of a version in review off; who and when are recorded (story E7).</summary>
public sealed record ApproveQuestion(CourseVersionId Version, QuestionId Question, PersonId Approver) : ICommand<Result<Unit, ApiError>>;
