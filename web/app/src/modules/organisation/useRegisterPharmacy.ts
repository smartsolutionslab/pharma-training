import { useQueryClient } from '@tanstack/vue-query';
import { useRouter } from 'vue-router';
import { z } from 'zod';
import {
  getGetOrganisationPharmaciesMineQueryKey,
  type RegisterPharmacyRequest,
  usePostOrganisationPharmacies,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { notify } from '@/lib/notify';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';

/**
 * Where a new pharmacy's founder goes next: the organisation module's page, its members and invitations once O6
 * replaces the generated samples there, at the same address.
 */
const teamPage = '/organisation';

/** What the user reads when the host refuses a registration, by its status. */
function refusal(error: unknown): string {
  if (error instanceof ApiError && error.status === 400) {
    return 'The pharmacy could not be registered: check its name and your job function.';
  }
  if (error instanceof ApiError && error.status === 403) {
    return 'Your account has no name or email address, so it cannot register a pharmacy.';
  }
  return 'The pharmacy could not be registered. Try again.';
}

/**
 * Registers a pharmacy with the user as its manager. Once the host has, the memberships are read again and only then
 * the new pharmacy chosen: chosen among memberships that do not hold it yet, it would be forgotten at once. The user is
 * told and led to the pharmacy's team page; a refusal is reported and leaves the user where they are.
 */
export function useRegisterPharmacy() {
  const queryClient = useQueryClient();
  const selected = useSelectedPharmacyStore();
  const router = useRouter();
  const { mutateAsync, isPending } = usePostOrganisationPharmacies();

  async function register(values: RegisterPharmacyRequest) {
    let id: string;
    try {
      id = z.uuid().parse(await mutateAsync({ data: values }));
    } catch (error) {
      notify.failure(refusal(error));
      return;
    }
    await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
    selected.choose(id);
    notify.success(`The pharmacy ${values.name} was registered.`);
    await router.push(teamPage);
  }

  return { register, isPending };
}
