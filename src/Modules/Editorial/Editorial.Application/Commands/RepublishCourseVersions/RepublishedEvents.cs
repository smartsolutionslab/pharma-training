namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

/// <summary>How many of each of Editorial's course-version events a republish sent.</summary>
public sealed record RepublishedEvents(int CourseVersions, int TestDefinitions, int Retirements);
