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
  | 'notFoundAfterRetry';

/**
 * What the page shows: the invitation on its way (or accepted and the page not moved on yet), or why it was not, with
 * the module's own words for a refusal the page has no text of its own for, and whether trying again can succeed.
 */
export type AcceptState =
  { kind: 'accepting' } | { kind: 'refused'; refusal: AcceptRefusal; serverMessage?: string; retryable?: boolean };

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
};

/**
 * The state a failed acceptance leaves the page in: a known refusal by its code, too many attempts by its status
 * whatever its code, and anything else failed, with a module's own words where the host sent them (ADR 0007). Only
 * too many attempts, a lost concurrent save, the host unavailable (5xx) and the network can succeed when tried again;
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
  const known = error.code === undefined ? undefined : refusalsByCode[error.code];
  if (known === 'notFound' && afterRetry) {
    return { kind: 'refused', refusal: 'notFoundAfterRetry' };
  }
  if (known === 'failed') {
    return { kind: 'refused', refusal: 'failed', retryable: true };
  }
  if (known !== undefined) {
    return { kind: 'refused', refusal: known };
  }
  const retryable = error.status >= 500;
  return error.serverMessage === undefined
    ? { kind: 'refused', refusal: 'failed', retryable }
    : { kind: 'refused', refusal: 'failed', serverMessage: error.serverMessage, retryable };
}

/** Where an employee goes once they have joined: Meine Schulungen. */
const coursesPage = '/learning';

/** Where the user goes when the pharmacy cannot be opened: the start page, whose switcher lists it. */
const startPage = '/';

/**
 * Accepts the invitation whose token the link's route kept for this tab. The token is taken out of storage once, as
 * the page is set up (and again by start, when a second link was opened while the page is shown), and kept in memory
 * for a retry only; it is posted once. Once accepted, the pharmacy is opened (useOpenPharmacy), the user told where
 * they now belong and led to Meine Schulungen, the address replaced so Back does not return to the page.
 */
export function useAcceptInvitation() {
  const queryClient = useQueryClient();
  const { open } = useOpenPharmacy();
  const router = useRouter();
  const { mutateAsync } = usePostOrganisationInvitationsAccept();

  let token: string | undefined;
  // Each attempt's number: an answer to an attempt a newer one replaced is not shown.
  let attempt = 0;
  const state = ref<AcceptState>({ kind: 'accepting' });

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
      await moveOn(answer);
    }
  }

  /** Takes the kept token out of storage and accepts it: as the page is set up, and for a second link. */
  function start() {
    token = takeInvitationToken();
    void accept(false);
  }

  async function moveOn(answer: unknown) {
    // 200 says the invitation was accepted, whatever the body: without the pharmacy's id it cannot be opened.
    const id = z.uuid().safeParse(answer);
    if (!id.success) {
      await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
    }
    if (!id.success || !(await open(id.data))) {
      notify.success(t('organisation.accept.acceptedNotOpened'));
      await router.replace(startPage);
      return;
    }
    notify.success(await acceptedIn(id.data));
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

  return { state, retry: () => accept(true), start };
}
