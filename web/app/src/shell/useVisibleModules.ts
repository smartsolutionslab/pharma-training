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

/** A page below a module the user may see: where it is and what it is called. */
export interface VisiblePage {
  to: string;
  title: string;
}

/**
 * The pages below the modules the user may see, in the order the app lists the modules: each by its own audience,
 * whatever its module's.
 */
export function useVisiblePages() {
  const { current } = useCurrentPharmacy();
  const isEditor = useHasRole('editor');
  return computed<VisiblePage[]>(() =>
    moduleRoutes.flatMap((route) =>
      (route.pages ?? [])
        .filter((page) => canReach(page.audience, { current: current.value, isEditor: isEditor.value }))
        .map((page) => ({ to: `/${route.path}/${page.path}`, title: page.title })),
    ),
  );
}
