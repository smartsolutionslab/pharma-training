using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>A module's DbContext as far as <see cref="RecordingOutbox"/> needs one: it is never opened.</summary>
public sealed class FlushDbContext(DbContextOptions<FlushDbContext> options) : DbContext(options);
