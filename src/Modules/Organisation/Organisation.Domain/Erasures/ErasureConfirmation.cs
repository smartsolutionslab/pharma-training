namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>What became of the confirmation mail of an erasure.</summary>
public enum ErasureConfirmation
{
    /// <summary>Not handled yet: the mail is due.</summary>
    Pending,

    /// <summary>Mailed to every address Organisation held for the person.</summary>
    Mailed,

    /// <summary>Organisation held no address for the person, so nothing was mailed.</summary>
    NoAddress,
}
