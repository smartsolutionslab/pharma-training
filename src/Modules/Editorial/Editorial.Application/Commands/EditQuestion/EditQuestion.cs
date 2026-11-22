using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;

/// <summary>Gives a question of a draft new content in its place; an approved one needs a review again.</summary>
public sealed record EditQuestion(CourseVersionId Version, QuestionId Question, DraftedQuestion Draft) : ICommand<Result<Unit, ApiError>>;
