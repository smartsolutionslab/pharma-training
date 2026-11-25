using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetErasure;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>Support reads an erasure back from the log: its request, its confirmation and its steps in order.</summary>
public class GetErasureTests
{
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_erasure_is_read_with_its_steps_in_order()
    {
        var store = new InMemoryPharmacyStore();
        var erasure = Erasure.Request(ErasureId.New(), Support, Timestamp.From(Start));
        erasure.ConfirmationHandled(ErasureConfirmation.Mailed, Timestamp.From(Start.AddMinutes(1)));
        erasure.CompleteStep(ErasureModule.From("compliance"), Timestamp.From(Start.AddMinutes(5)));
        erasure.CompleteStep(ErasureModule.Organisation, Timestamp.From(Start.AddMinutes(1)));
        erasure.CompleteStep(ErasureModule.From("certification"), Timestamp.From(Start.AddMinutes(1)));
        store.StoredErasures.Add(erasure);

        var read = await new GetErasureHandler(store).HandleAsync(new GetErasure(erasure.Id.Value), TestContext.Current.CancellationToken);

        read.IsSuccess.ShouldBeTrue();
        var dto = read.Value;
        dto.Id.ShouldBe(erasure.Id.Value);
        dto.RequestedAt.ShouldBe(Start);
        dto.RequestedBy.ShouldBe(Support.Value);
        dto.ConfirmationMailed.ShouldBeTrue();
        dto.ConfirmationHandledAt.ShouldBe(Start.AddMinutes(1));
        dto.Steps.Select(step => (step.Module, step.CompletedAt)).ShouldBe(
        [
            ("certification", Start.AddMinutes(1)),
            ("organisation", Start.AddMinutes(1)),
            ("compliance", Start.AddMinutes(5)),
        ]);
    }

    [Fact]
    public async Task An_erasure_whose_person_had_no_address_was_not_mailed()
    {
        var store = new InMemoryPharmacyStore();
        var erasure = Erasure.Request(ErasureId.New(), Support, Timestamp.From(Start));
        erasure.ConfirmationHandled(ErasureConfirmation.NoAddress, Timestamp.From(Start.AddMinutes(1)));
        store.StoredErasures.Add(erasure);

        var read = await new GetErasureHandler(store).HandleAsync(new GetErasure(erasure.Id.Value), TestContext.Current.CancellationToken);

        read.Value.ConfirmationMailed.ShouldBeFalse();
        read.Value.ConfirmationHandledAt.ShouldBe(Start.AddMinutes(1));
        read.Value.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_erasure_answers_not_found()
    {
        var handler = new GetErasureHandler(new InMemoryPharmacyStore());

        var unknown = await handler.HandleAsync(new GetErasure(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        var empty = await handler.HandleAsync(new GetErasure(Guid.Empty), TestContext.Current.CancellationToken);

        unknown.Error.Code.ShouldBe("organisation.erasure-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        empty.Error.Code.ShouldBe("organisation.erasure-not-found");
    }
}
