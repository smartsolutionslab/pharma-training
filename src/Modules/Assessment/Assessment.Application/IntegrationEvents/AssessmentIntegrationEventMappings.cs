using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;

/// <summary>What Assessment announces of its examinations. Stub for the red run of #46: not implemented yet.</summary>
public static class AssessmentIntegrationEventMappings
{
    public static TestPassedV1 ToIntegrationEvent(this ExaminationPassed passed) => throw new NotImplementedException();
}
