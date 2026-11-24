using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The markers of the stored events Assessment announced (#277), in memory. Each one added is recorded in the steps the
/// aggregate store and <see cref="RecordingAssessmentUnitOfWork"/> share, so a test sees that the marker is added before
/// the commit that carries its announcement: committing the module's unit of work stores both together.
/// </summary>
internal sealed class InMemoryAnnouncedEvents(List<string> steps) : IAnnouncedEvents
{
    public const string Marked = "mark";

    public List<AnnouncedEvent> Added { get; } = [];

    public void Add(AnnouncedEvent announced)
    {
        Added.Add(announced);
        steps.Add(Marked);
    }
}
