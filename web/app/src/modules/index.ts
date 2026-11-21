import type { Component } from 'vue';

/**
 * Who a page is for (docs/domain/README.md, "Vue-Seiten"): a member of the chosen pharmacy, a member who may manage it
 * (manage members or assign courses), or the realm role editor, whatever the pharmacy.
 */
export type Audience = 'employee' | 'manager' | 'editor';

/** What src/modules/<module>/routes.ts exports as `route`; the app finds every module's file by itself (no list to edit). */
export interface ModuleRoute {
  path: string;
  title: string;
  /** Who the page is for; the shell shows it to them only. Hiding is a convenience: the backend's policies decide. */
  audience: Audience;
  component: Component;
}

export const moduleRoutes = Object.values(
  import.meta.glob<ModuleRoute>('./*/routes.ts', { eager: true, import: 'route' }),
);
