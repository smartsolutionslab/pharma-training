import { useQueryClient } from '@tanstack/vue-query';
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { z } from 'zod';
import {
  getGetOrganisationPharmaciesIdQueryOptions,
  getGetOrganisationPharmaciesMineQueryKey,
  usePostOrganisationInvitationsAccept,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import { takeInvitationToken } from './invitationLink';
import { useOpenPharmacy } from './useOpenPharmacy';

/** Why the invitation was not accepted, each a state of the page with its title and text in the catalogs. */
export type AcceptRefusal =
  | 'missing'
  | 'notFound'
  | 'expired'
  | 'anotherAddress'
  | 'emailNotVerified'
  | 'identityIncomplete'
  | 'alreadyMember'
  | 'tooMany'
  | 'failed'
  | 'invitationChanged'
  | 'notFoundAfterRetry';

/**
 * What the page shows: a kept token ready to be accepted when the user presses the button, the invitation on its way
 * (or accepted and the page not moved on yet), or why it was not, with the message for a refusal's code the page has
 * no state of its own for (src/i18n/errorText.ts, #147), and whether trying again can succeed.
 */
export type AcceptState =
  | { kind: 'ready' }
  | { kind: 'accepting' }
  | { kind: 'refused'; refusal: AcceptRefusal; message?: ErrorMessage; retryable?: boolean };

/** The organisation module's refusals the page knows, by code (POST /organisation/invitations/accept). */
const refusalsByCode: Record<string, AcceptRefusal> = {
  'organisation.invalid-invitation-token': 'missing',
  'organisation.invitation-not-found': 'notFound',
  'organisation.invitation-expired': 'expired',
  'organisation.invitation-for-another-address': 'anotherAddress',
  'organisation.email-not-verified': 'emailNotVerified',
  'organisation.identity-incomplete': 'identityIncomplete',
  'organisation.person-already-member': 'alreadyMember',
  // Lost a concurrent save (ADR 0002): worth trying again.
  'organisation.pharmacy-changed': 'failed',
  // Lost to a save at the same moment (a deadlock): nothing was saved, worth trying again.
  'organisation.concurrent-save': 'failed',
  // The invitation lost a concurrent save: worth trying again, with words of its own.
  'organisation.invitation-changed': 'invitationChanged',
};

/**
 * The state a failed acceptance leaves the page in: a known refusal by its code, too many attempts by its status
 * whatever its code, and anything else failed, with the message for its code (errorMessage, #147). Only
 * too many attempts, a lost concurrent save (already-exists included), the host unavailable (5xx) and the network can
 * succeed when tried again;
 * any other refusal of the request (a framework's invalid-argument, a module's code the page does not know) is final.
 * A Retry answered not found may follow a first attempt that was accepted although its answer never arrived.
 */
function refusedBy(error: unknown, afterRetry: boolean): AcceptState {
  if (!(error instanceof ApiError)) {
    return { kind: 'refused', refusal: 'failed', retryable: true };
  }
  if (error.status === 429) {
    return { kind: 'refused', refusal: 'tooMany', retryable: true };
  }
  // Accepting creates nothing the user could reload to find: the unique index refused a membership saved at the same
  // moment, a lost race like pharmacy-changed, with the failed state's own text.
  if (error.failureCode === 'already-exists') {
    return { kind: 'refused', refusal: 'failed', retryable: true };
  }
  const known = error.code === undefined ? undefined : refusalsByCode[error.code];
  if (known === 'notFound' && afterRetry) {
    return { kind: 'refused', refusal: 'notFoundAfterRetry' };
  }
  if (known === 'failed' || known === 'invitationChanged') {
    return { kind: 'refused', refusal: known, retryable: true };
  }
  if (known !== undefined) {
    return { kind: 'refused', refusal: known };
  }
  return {
    kind: 'refused',
    refusal: 'failed',
    message: errorMessage(error, 'organisation.accept.failed.text'),
    retryable: error.status >= 500,
  };
}

/** Where an employee goes once they have joined: Meine Schulungen. */
const coursesPage = '/learning';

/** Where the user goes when the pharmacy cannot be opened: the start page, whose switcher lists it. */
const startPage = '/';

/**
 * Accepts the invitation whose token the link's route kept for this tab. The token is taken out of storage once, as
 * the page is set up (and again by start, when a second link was opened while the page is shown), and kept in memory
 * for the button and a retry only; it is posted once, when the user presses the button (confirm), never as the page
 * opens. Nothing about the pharmacy is read before that. Once accepted, the pharmacy is opened (useOpenPharmacy), the
 * user told where they now belong and led to Meine Schulungen, the address replaced so Back does not return to the
 * page; unless a second link was opened meanwhile, which then stays ready and wins.
 */
export function useAcceptInvitation() {
  const queryClient = useQueryClient();
  const { open } = useOpenPharmacy();
  const router = useRouter();
  const { mutateAsync } = usePostOrganisationInvitationsAccept();

  let token: string | undefined;
  // Each attempt's number: an answer to an attempt a newer one replaced is not shown.
  let attempt = 0;
  const state = ref<AcceptState>({ kind: 'ready' });

  async function accept(afterRetry: boolean) {
    const current = ++attempt;
    if (token === undefined) {
      state.value = { kind: 'refused', refusal: 'missing' };
      return;
    }
    state.value = { kind: 'accepting' };
    let answer: unknown;
    try {
      answer = await mutateAsync({ data: { token } });
    } catch (error) {
      if (current === attempt) {
        state.value = refusedBy(error, afterRetry);
      }
      return;
    }
    if (current === attempt) {
      await moveOn(answer, current);
    }
  }

  /**
   * Takes the kept token out of storage and shows it ready to accept, or says to open the link again without one: as
   * the page is set up, and for a second link, whose token replaces the one before and any answer still on its way.
   */
  function start() {
    attempt++;
    token = takeInvitationToken();
    state.value = token === undefined ? { kind: 'refused', refusal: 'missing' } : { kind: 'ready' };
  }

  /** The button's press: accepts the ready token once, however often it is pressed before the page renders again. */
  function confirm() {
    if (state.value.kind !== 'ready') {
      return;
    }
    void accept(false);
  }

  /** Retry's press: posts the kept token again once, and only from a refusal that trying again can turn round. */
  function retry() {
    if (state.value.kind !== 'refused' || state.value.retryable !== true) {
      return;
    }
    void accept(true);
  }

  /**
   * Opens the pharmacy joined and leads on. A second link opened while this awaits (attempt no longer current) wins:
   * the page stays at its ready state and neither announces nor leaves for the first.
   */
  async function moveOn(answer: unknown, current: number) {
    // 200 says the invitation was accepted, whatever the body: without the pharmacy's id it cannot be opened.
    const id = z.uuid().safeParse(answer);
    if (!id.success) {
      await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
    }
    if (!id.success || !(await open(id.data))) {
      if (current === attempt) {
        notify.success(t('organisation.accept.acceptedNotOpened'));
        await router.replace(startPage);
      }
      return;
    }
    const told = await acceptedIn(id.data);
    if (current !== attempt) {
      return;
    }
    notify.success(told);
    await router.replace(coursesPage);
  }

  /** What the user is told: the pharmacy's name where it can be read, else only that the invitation was accepted. */
  async function acceptedIn(id: string) {
    try {
      const pharmacy = await queryClient.fetchQuery(getGetOrganisationPharmaciesIdQueryOptions(id));
      return t('organisation.accept.accepted', { name: pharmacy.name });
    } catch {
      return t('organisation.accept.acceptedUnnamed');
    }
  }

  start();

  return { state, confirm, retry, start };
}
