using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveQuestion;

/// <summary>Removes a question from a draft; the others keep their order.</summary>
public sealed record RemoveQuestion(CourseVersionId Version, QuestionId Question) : ICommand<Result<Unit, ApiError>>;
