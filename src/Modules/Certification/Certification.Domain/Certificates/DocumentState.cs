namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>Whether the PDF exists yet. It is rendered once, right after issuing, by a reaction.</summary>
public enum DocumentState
{
    Pending,
    Stored,
}
