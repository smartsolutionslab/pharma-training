import { computed, type ComputedRef } from 'vue';
import { useGetMe } from '@/api/generated';

/** The signed-in user's name as the host's /me answers it; undefined until it has answered, or when it names nobody. */
export function useCurrentUser(): ComputedRef<string | undefined> {
  const me = useGetMe();
  return computed(() => me.data.value?.name ?? undefined);
}
