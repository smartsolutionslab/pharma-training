using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;

/// <summary>Drafts the next version of a published one, by this editor, and answers the new version's id.</summary>
public sealed record DraftNextVersion(CourseVersionId Version, PersonId Editor) : ICommand<Result<Guid, ApiError>>;
