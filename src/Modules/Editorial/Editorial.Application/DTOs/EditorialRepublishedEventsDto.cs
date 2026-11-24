namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// What a republish of Editorial's course-version events sent: the course versions, their test definitions, the file
/// releases of those with PDF or video chapters, and the retirements, each counted once per event.
/// </summary>
public sealed record EditorialRepublishedEventsDto(int CourseVersions, int TestDefinitions, int FileReleases, int Retirements);
