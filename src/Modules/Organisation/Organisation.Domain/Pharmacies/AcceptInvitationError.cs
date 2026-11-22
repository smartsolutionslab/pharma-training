namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Every way <see cref="Pharmacy.AcceptInvitation"/> refuses; a new one fails the build until it is mapped.</summary>
public union AcceptInvitationError(InvitationNotFound, InvitationExpired, InvitationForAnotherAddress, PersonAlreadyMember);
