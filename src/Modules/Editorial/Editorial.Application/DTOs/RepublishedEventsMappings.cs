using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>What a republish sent, as the module's endpoint answers it.</summary>
public static class RepublishedEventsMappings
{
    public static EditorialRepublishedEventsDto ToDto(this RepublishedEvents events)
    {
        Ensure.That(events).IsNotNull();

        return new EditorialRepublishedEventsDto(events.CourseVersions, events.TestDefinitions, events.Retirements);
    }
}
