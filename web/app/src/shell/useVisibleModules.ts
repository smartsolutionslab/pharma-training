import { computed } from 'vue';
import { useHasRole } from '@/auth/useHasRole';
import { moduleRoutes } from '@/modules';
import { canReach } from './audience';
import { useCurrentPharmacy } from './useCurrentPharmacy';

/** The modules' routes the user may see, in the order the app lists them; the start page is always there besides. */
export function useVisibleModules() {
  const { current } = useCurrentPharmacy();
  const isEditor = useHasRole('editor');
  return computed(() =>
    moduleRoutes.filter((route) => canReach(route.audience, { current: current.value, isEditor: isEditor.value })),
  );
}
