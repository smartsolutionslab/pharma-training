namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>A module's unit of work over <see cref="FlushDbContext"/>, as every module derives its own.</summary>
public sealed class FlushUnitOfWork(IServiceProvider services) : ModuleUnitOfWork<FlushDbContext>(services);
