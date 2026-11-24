using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The markers of the stored events Assessment announced (#277); implemented in its Infrastructure project.</summary>
public interface IAnnouncedEvents
{
    /// <summary>Records the event as announced; committing the module's unit of work stores it with the announcement.</summary>
    void Add(AnnouncedEvent announced);
}
