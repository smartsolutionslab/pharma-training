namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>Every way an assignment's own steps (Assign, ChangeDueDate, Cancel, Complete) refuse; a new one fails the build until it is mapped.</summary>
public union AssignmentError(DueDateInThePast, AssignmentNotActive, CertificatePredatesAssignment);
