using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

/// <summary>Caches one model per shape, so each test of <see cref="MarkedModelContext"/> builds the model it asks for.</summary>
public sealed class MarkedModelCacheKeys : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is MarkedModelContext marked ? (marked.Shape, designTime) : (context.GetType(), designTime);
}
