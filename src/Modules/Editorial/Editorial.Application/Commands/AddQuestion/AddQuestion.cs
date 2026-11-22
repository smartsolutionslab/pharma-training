using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddQuestion;

/// <summary>Adds a question about one of a draft's chapters after its others, written by a person and unreviewed; answers its id.</summary>
public sealed record AddQuestion(CourseVersionId Version, DraftedQuestion Draft) : ICommand<Result<Guid, ApiError>>;
