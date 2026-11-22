import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { z } from 'zod';
import {
  getGetOrganisationPharmaciesMineQueryKey,
  getGetOrganisationPharmaciesMineQueryOptions,
  type RegisterPharmacyRequest,
  usePostOrganisationPharmacies,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t } from '@/i18n';
import { notify } from '@/lib/notify';
import { myPharmaciesParams } from '@/shell/useMyPharmacies';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';

/**
 * Where a new pharmacy's founder goes next: the organisation module's page, its members and invitations once O6
 * replaces the generated samples there, at the same address.
 */
const teamPage = '/organisation';

/** Where the user goes when the new pharmacy cannot be opened: the start page, whose switcher lists it. */
const startPage = '/';

/**
 * What the user reads when a registration is refused: the host's own words as it sent them (until #147 translates its
 * codes), or the catalog's when it sent none (a network failure, a 5xx without a body).
 */
function refusal(error: unknown): string {
  if (error instanceof ApiError && error.serverMessage !== undefined) {
    return error.serverMessage;
  }
  return t('organisation.register.failed');
}

/**
 * Registers a pharmacy with the user as its manager. Once the host has, the memberships are read again and only then
 * the new pharmacy chosen: chosen among memberships that do not hold it yet, it would be forgotten at once. The user is
 * told and led to the pharmacy's team page. A refusal is reported and leaves the user where they are; a pharmacy that
 * was registered but cannot be opened (no id in the answer, or not among the memberships read again) is not chosen, and
 * the user goes to the start page. The returned promise settles once the page has moved on.
 */
export function useRegisterPharmacy() {
  const queryClient = useQueryClient();
  const selected = useSelectedPharmacyStore();
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

    // Marked stale without a refetch of its own: the read below is the one every observer of the memberships shares.
    await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey(), refetchType: 'none' });
    let opened = false;
    try {
      const mine = await queryClient.fetchQuery(getGetOrganisationPharmaciesMineQueryOptions(myPharmaciesParams));
      opened = mine.items.some((membership) => membership.id === id.data);
    } catch {
      // The memberships could not be read again: the new pharmacy cannot be opened.
    }
    if (!opened) {
      notify.success(t('organisation.register.registeredNotOpened', { name: values.name }));
      await router.push(startPage);
      return;
    }
    selected.choose(id.data);
    notify.success(t('organisation.register.registered', { name: values.name }));
    await router.push(teamPage);
  }

  /** The registration is on its way, or registered and the page not moved on yet. */
  const isBusy = computed(() => isPending.value || isOpening.value);

  return { register, isBusy };
}
