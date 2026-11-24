import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage, messageText } from '@/i18n/errorText';

/** An action of the members and invitations page (#12). */
export type TeamAction = 'invite' | 'renew' | 'revoke' | 'change' | 'end';

/**
 * The page's own text for a code: the unique index's already-exists reports the same fact as the handler's own check,
 * already-member-or-invited, so the page says the same.
 */
const overrides: Record<string, MessageKey> = {
  'already-exists': 'errors.organisation.already-member-or-invited',
};

/** Each action's text when it failed for a reason the page has no words of its own for. */
const failed: Record<TeamAction, MessageKey> = {
  invite: 'organisation.team.invite.failed',
  renew: 'organisation.team.invitations.renewFailed',
  revoke: 'organisation.team.revoke.failed',
  change: 'organisation.team.change.failed',
  end: 'organisation.team.end.failed',
};

/**
 * What the page says when an action is refused or fails, by the refusal's code (src/i18n/errorText.ts, #147): the
 * catalog's errors.<code>, already-exists as already-member-or-invited, the host's words only for a module code the
 * catalog does not know; anything else, a network failure included, that the action failed, with the values its text
 * names.
 */
export function teamFailureKey(action: TeamAction, error: unknown, values: Record<string, unknown> = {}): ErrorMessage {
  return errorMessage(error, failed[action], { values, overrides });
}

/** The text of a failure, in the language of the moment. */
export function teamFailureText(failure: ErrorMessage): string {
  return messageText(failure);
}

/** Refusals after which the membership itself may be gone or changed: the caller's memberships are read again. */
const membershipCodes = ['organisation.pharmacy-not-found', 'organisation.capability-missing'];

/** Whether the host refused because the caller's membership is gone or may no longer manage members. */
export function refusesMembership(error: unknown): error is ApiError & { code: string } {
  return error instanceof ApiError && error.code !== undefined && membershipCodes.includes(error.code);
}

/** Why a list could not be read: a refusal by its code, anything else that it failed. */
export function teamLoadFailureKey(error: unknown, fallback: MessageKey): ErrorMessage {
  return errorMessage(error, fallback);
}
