import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { ApiError } from '@/api/baseApi';
import {
  getGetCertificationPharmaciesIdRetentionPeriodQueryKey,
  useGetCertificationPharmaciesIdRetentionPeriod,
  usePutCertificationPharmaciesIdRetentionPeriod,
} from '@/api/generated';

/** What saving the period came to: ok, or a failure carrying what the call rejected with (shown by retentionRefusal). */
export type RetentionChange = { ok: true } | { ok: false; error: unknown };

/** A refusal (4xx) is final: asking again gets the same answer. Anything else is asked up to three times more. */
const retry = (count: number, error: unknown) => !(error instanceof ApiError && error.status < 500) && count < 3;

/**
 * The pharmacy's retention period (GET /certification/pharmacies/{id}/retention-period, #418), read only while a
 * pharmacy is current, and saving it (PUT, 204 without a body, ADR 0039). After a save, refused or not, the period is
 * read again: what the page shows comes from the read alone.
 */
export function useRetentionPeriod(pharmacyId: MaybeRefOrGetter<string | undefined>) {
  const period = useGetCertificationPharmaciesIdRetentionPeriod(() => toValue(pharmacyId) ?? '', {
    query: { enabled: computed(() => toValue(pharmacyId) !== undefined), retry },
  });
  const queryClient = useQueryClient();
  const saving = usePutCertificationPharmaciesIdRetentionPeriod({
    mutation: {
      onSettled: (_data, _error, { id }) =>
        queryClient.invalidateQueries({ queryKey: getGetCertificationPharmaciesIdRetentionPeriodQueryKey(id) }),
    },
  });

  /** Stores the period for the pharmacy. */
  async function save(id: string, years: number): Promise<RetentionChange> {
    try {
      await saving.mutateAsync({ id, data: { years } });
      return { ok: true };
    } catch (error) {
      return { ok: false, error };
    }
  }

  return { period, save, isSaving: saving.isPending };
}
