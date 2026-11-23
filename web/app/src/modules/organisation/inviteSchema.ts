import { z } from 'zod';
import { OrganisationJobFunction, OrganisationMembershipRole } from '@/api/generated';
import type { MessageKey } from '@/i18n';

/**
 * An address of the backend's shape (EmailAddress.Shape): one @, a dot after it, and none of the characters mail reads
 * as syntax, whitespace or a NUL.
 */
const emailShape = /^[^@\s<>"(),;:[\]\\]+@[^@\s<>"(),;:[\]\\]+\.[^@\s<>"(),;:[\]\\]+$/;

/** At most as long as the backend stores an address (EmailAddress.MaxLength). */
export const emailMaxLength = 254;

// Each message is a key, rendered with t where it is shown: a message already shown follows a switch of the language.
/** The invite form, checked as the host checks the request (InviteOrganisationMemberRequest) before it checks it again. */
export const inviteSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, { error: 'organisation.team.invite.emailRequired' satisfies MessageKey })
    .max(emailMaxLength, { error: 'organisation.team.invite.emailTooLong' satisfies MessageKey })
    .refine((email) => emailShape.test(email) && !email.includes('\u0000'), {
      error: 'organisation.team.invite.emailInvalid' satisfies MessageKey,
    }),
  jobFunction: z.enum(OrganisationJobFunction, {
    error: 'organisation.team.invite.jobFunctionRequired' satisfies MessageKey,
  }),
  role: z.enum(OrganisationMembershipRole, { error: 'organisation.team.invite.roleRequired' satisfies MessageKey }),
});

/** An empty form: no address, no job function chosen, and the role most invitations have. */
export const inviteInitialValues: {
  email: string;
  jobFunction: OrganisationJobFunction | '';
  role: OrganisationMembershipRole;
} = { email: '', jobFunction: '', role: OrganisationMembershipRole.Employee };

/** The job functions in the order the selects offer them, labelled by the glossary. */
export const jobFunctions = [
  OrganisationJobFunction.Pharmacist,
  OrganisationJobFunction.Pta,
  OrganisationJobFunction.Pka,
  OrganisationJobFunction.Trainee,
  OrganisationJobFunction.Other,
] as const;

/** The membership roles in the order the selects offer them, labelled by the glossary. */
export const membershipRoles = [OrganisationMembershipRole.Manager, OrganisationMembershipRole.Employee] as const;
