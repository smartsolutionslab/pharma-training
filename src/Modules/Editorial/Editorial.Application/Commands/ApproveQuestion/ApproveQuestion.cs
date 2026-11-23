using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;

/// <summary>
/// An editor signs a question of a draft or of a version in review off; who, under which display name, and when are
/// recorded (story E7, #205).
/// </summary>
public sealed record ApproveQuestion(CourseVersionId Version, QuestionId Question, PersonId Approver, ReviewerName ApproverName) : ICommand<Result<Unit, ApiError>>;
