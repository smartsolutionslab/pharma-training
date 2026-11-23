import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { z } from 'zod';
import {
  getGetOrganisationPharmaciesMineQueryKey,
  type RegisterPharmacyRequest,
  usePostOrganisationPharmacies,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t, type MessageKey } from '@/i18n';
import { notify } from '@/lib/notify';
import { useOpenPharmacy } from './useOpenPharmacy';

/**
 * Where a new pharmacy's founder goes next: the organisation module's page, its members and invitations once O6
 * replaces the generated samples there, at the same address.
 */
const teamPage = '/organisation';

/** Where the user goes when the new pharmacy cannot be opened: the start page, whose switcher lists it. */
const startPage = '/';

/** The refusals the page tells in its own words, by the host's code; every other code is #147's to translate. */
const refusalsByCode: Record<string, MessageKey> = {
  'organisation.email-not-verified': 'organisation.register.emailNotVerified',
};

/**
 * What the user reads when a registration is refused: the catalog's text for a code the page has words of its own for,
 * else the host's own words as it sent them (until #147 translates its codes), or the catalog's general text when it
 * sent none (a network failure, a 5xx without a body).
 */
function refusal(error: unknown): string {
  if (error instanceof ApiError) {
    const key = error.code === undefined ? undefined : refusalsByCode[error.code];
    if (key !== undefined) {
      return t(key);
    }
  }
  if (error instanceof ApiError && error.serverMessage !== undefined) {
    return error.serverMessage;
  }
  return t('organisation.register.failed');
}

/**
 * Registers a pharmacy with the user as its manager. Once the host has, the new pharmacy is opened (useOpenPharmacy:
 * chosen only once the memberships read again hold it). The user is told and led to the pharmacy's team page. A
 * refusal is reported and leaves the user where they are; a pharmacy that was registered but cannot be opened (no id in
 * the answer, or not among the memberships read again) is not chosen, and the user goes to the start page. The
 * returned promise settles once the page has moved on.
 */
export function useRegisterPharmacy() {
  const queryClient = useQueryClient();
  const { open: openPharmacy } = useOpenPharmacy();
  const router = useRouter();
  const { mutateAsync, isPending } = usePostOrganisationPharmacies();
  // From the host's answer until the page has moved on: the memberships are read again meanwhile.
  const isOpening = ref(false);

  async function register(values: RegisterPharmacyRequest) {
    let answer: unknown;
    try {
      answer = await mutateAsync({ data: values });
    } catch (error) {
      notify.failure(refusal(error));
      return;
    }
    isOpening.value = true;
    try {
      await open(values, answer);
    } finally {
      isOpening.value = false;
    }
  }

  async function open(values: RegisterPharmacyRequest, answer: unknown) {
    // 201 says the pharmacy exists, whatever the body: without its id it cannot be chosen, but it was registered.
    const id = z.uuid().safeParse(answer);
    if (!id.success) {
      await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
      notify.success(t('organisation.register.registered', { name: values.name }));
      await router.push(startPage);
      return;
    }

    if (!(await openPharmacy(id.data))) {
      notify.success(t('organisation.register.registeredNotOpened', { name: values.name }));
      await router.push(startPage);
      return;
    }
    notify.success(t('organisation.register.registered', { name: values.name }));
    await router.push(teamPage);
  }

  /** The registration is on its way, or registered and the page not moved on yet. */
  const isBusy = computed(() => isPending.value || isOpening.value);

  return { register, isBusy };
}
