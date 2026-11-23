using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;

/// <summary>
/// A version in review goes back to its editors as a draft, and takes changes again; who returns it, when and why is
/// stored as a record of its own, in the same save, with the name the editor's token carries (#220).
/// </summary>
public sealed record ReturnToDraft(CourseVersionId Version, PersonId Editor, ReviewerName EditorName, ReturnReason Reason) : ICommand<Result<Unit, ApiError>>;
