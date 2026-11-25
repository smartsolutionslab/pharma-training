using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;

/// <summary>That a person was erased (#115), and when: a tombstone that holds no name.</summary>
public sealed class ErasedPerson
{
    private ErasedPerson(PersonId person, Timestamp erasedAt)
    {
        Person = person;
        ErasedAt = erasedAt;
    }

    public PersonId Person { get; }

    public Timestamp ErasedAt { get; }

    public static ErasedPerson Of(PersonId person, Timestamp erasedAt)
    {
        Ensure.That(person).IsNotNull();
        Ensure.That(erasedAt).IsNotNull();
        return new ErasedPerson(person, erasedAt);
    }
}
