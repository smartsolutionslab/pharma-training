using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

/// <summary>
/// A change kept to its owner's row version climbs through the one foreign key of its entity (docs/adr/0037). On an
/// entity with more than one, the nearest row version would depend on the model's key order, so the model refuses the
/// marker there when it is built.
/// </summary>
public sealed class OwnersOnlyRowVersionTests
{
    [Fact]
    public void A_marked_entity_with_one_foreign_key_builds()
    {
        using var context = new MarkedModelContext(MarkedModel.OwnedLinkWithOneKey);

        context.Model.FindEntityType(typeof(Link)).ShouldNotBeNull();
    }

    [Fact]
    public void A_marked_entity_with_two_foreign_keys_is_refused_when_the_model_is_built()
    {
        using var context = new MarkedModelContext(MarkedModel.EntityWithTwoKeys);

        var refused = Should.Throw<InvalidOperationException>(() => context.Model);

        refused.Message.ShouldContain(nameof(Child));
        refused.Message.ShouldContain("foreign key");
    }

    [Fact]
    public void A_marked_property_of_an_entity_with_two_foreign_keys_is_refused_when_the_model_is_built()
    {
        using var context = new MarkedModelContext(MarkedModel.PropertyOnTwoKeys);

        var refused = Should.Throw<InvalidOperationException>(() => context.Model);

        refused.Message.ShouldContain(nameof(Child));
        refused.Message.ShouldContain(nameof(Child.Note));
    }
}
