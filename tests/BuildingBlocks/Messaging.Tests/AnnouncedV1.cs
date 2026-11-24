using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>An integration event the flush tests publish, told apart by its number.</summary>
public sealed record AnnouncedV1(int Number) : IntegrationEvent;
