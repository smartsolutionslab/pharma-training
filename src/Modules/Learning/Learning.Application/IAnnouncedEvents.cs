using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>The markers of the stored events Learning announced (#277); implemented in its Infrastructure project.</summary>
public interface IAnnouncedEvents
{
    /// <summary>Records the event as announced; committing the module's unit of work stores it with the announcement.</summary>
    void Add(AnnouncedEvent announced);
}
