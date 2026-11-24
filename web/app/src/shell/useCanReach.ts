import { computed, type ComputedRef, type MaybeRefOrGetter, toValue } from 'vue';
import { useHasRole } from '@/auth/useHasRole';
import type { Audience } from '@/modules';
import { canReach } from './canReach';
import { useCurrentPharmacy } from './useCurrentPharmacy';

/**
 * Whether the signed-in user may see a page for the audience, by the shell's own rule (canReach): for a page that links
 * to another module's page only when the user may open it. It reads no module's routes, so a module's page may ask it.
 */
export function useCanReach(audience: MaybeRefOrGetter<Audience>): ComputedRef<boolean> {
  const { current } = useCurrentPharmacy();
  const isEditor = useHasRole('editor');
  return computed(() => canReach(toValue(audience), { current: current.value, isEditor: isEditor.value }));
}
