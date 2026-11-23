import { ApiError } from '@/api/baseApi';
import { type MessageKey, t } from '@/i18n';

/** An action of the members and invitations page (#12). */
export type TeamAction = 'invite' | 'renew' | 'revoke' | 'change' | 'end';

/** What the page says for a failure: a catalog key with its values, or the host's own words (ADR 0007). */
export type TeamFailure = { key: MessageKey; values: Record<string, unknown> } | { message: string };

/** The page's text for a refusal the module names by its code. */
const byCode: Record<string, MessageKey> = {
  'organisation.last-manager': 'organisation.team.errors.lastManager',
  'organisation.already-member-or-invited': 'organisation.team.errors.alreadyMemberOrInvited',
  'organisation.no-open-invitation': 'organisation.team.errors.noOpenInvitation',
  'organisation.no-active-membership': 'organisation.team.errors.noActiveMembership',
  'organisation.capability-missing': 'organisation.team.errors.capabilityMissing',
  'organisation.pharmacy-not-found': 'organisation.team.errors.pharmacyNotFound',
  'organisation.pharmacy-changed': 'organisation.team.errors.pharmacyChanged',
  'organisation.invalid-email': 'organisation.team.errors.invalidEmail',
  'organisation.invalid-job-function': 'organisation.team.errors.invalidJobFunction',
  'organisation.invalid-membership-role': 'organisation.team.errors.invalidMembershipRole',
  'organisation.identity-incomplete': 'organisation.team.errors.identityIncomplete',
  'organisation.invitation-renewed-too-soon': 'organisation.team.errors.invitationRenewedTooSoon',
  'organisation.too-many-invitations-today': 'organisation.team.errors.tooManyInvitationsToday',
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
 * What the page says when an action is refused or fails: a module code it knows gives its text; the unique index's
 * already-exists the same as already-member-or-invited, since it reports the same fact; another module code the host's
 * own words; anything else, a network failure included, that the action failed, with the values its text names.
 */
export function teamFailureKey(action: TeamAction, error: unknown, values: Record<string, unknown> = {}): TeamFailure {
  if (!(error instanceof ApiError)) {
    return { key: failed[action], values };
  }
  const known = error.code === undefined ? undefined : byCode[error.code];
  if (known !== undefined) {
    return { key: known, values };
  }
  if (error.failureCode === 'already-exists') {
    return { key: 'organisation.team.errors.alreadyMemberOrInvited', values };
  }
  if (error.serverMessage !== undefined) {
    return { message: error.serverMessage };
  }
  return { key: failed[action], values };
}

/** The text of a failure, in the language of the moment. */
export function teamFailureText(failure: TeamFailure): string {
  return 'message' in failure ? failure.message : t(failure.key, failure.values);
}

/** Why a list could not be read: a refusal the page has words for says what it is, anything else that it failed. */
export function teamLoadFailureKey(error: unknown, fallback: MessageKey): MessageKey {
  if (error instanceof ApiError && error.code !== undefined) {
    if (error.code === 'organisation.pharmacy-not-found' || error.code === 'organisation.capability-missing') {
      return byCode[error.code] ?? fallback;
    }
  }
  return fallback;
}
