import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { z } from 'zod';
import {
  getGetOrganisationPharmaciesMineQueryKey,
  type RegisterPharmacyRequest,
  usePostOrganisationPharmacies,
} from '@/api/generated';
import { t, type MessageKey } from '@/i18n';
import { errorText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import { useOpenPharmacy } from './useOpenPharmacy';

/**
 * Where a new pharmacy's founder goes next: the organisation module's page, its members and invitations once O6
 * replaces the generated samples there, at the same address.
 */
const teamPage = '/organisation';

/** Where the user goes when the new pharmacy cannot be opened: the start page, whose switcher lists it. */
const startPage = '/';

/** The refusals the page tells in its own words, by the host's code; every other code reads the catalog's (#147). */
const overrides: Record<string, MessageKey> = {
  'organisation.email-not-verified': 'organisation.register.emailNotVerified',
};

/**
 * What the user reads when a registration is refused, by its code (src/i18n/errorText.ts): the page's own words where
 * it has some, else the catalog's errors.<code>, the host's words only for a module code the catalog does not know,
 * and the page's general text for anything else (a network failure, a 5xx).
 */
function refusal(error: unknown): string {
  return errorText(error, 'organisation.register.failed', { overrides });
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
