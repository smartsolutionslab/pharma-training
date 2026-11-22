import type { Component } from 'vue';
import type { MessageKey } from '@/i18n';

/**
 * Who a page is for (docs/domain/README.md, "Vue-Seiten"): a member of the chosen pharmacy, a member who may manage it
 * (manage members or assign courses), the realm role editor, whatever the pharmacy, or every signed-in user, with or
 * without a pharmacy or a role.
 */
export type Audience = 'employee' | 'manager' | 'editor' | 'signed-in';

/**
 * A page of the app: its address segment, its title's key in the catalogs (shown in the language of the moment in the
 * sidebar, the breadcrumbs and the palette) and audience.
 */
export interface ModulePage {
  /** One segment: below the module for a module's page, the module's own for the module. */
  path: string;
  /** modules.<module>.title for a module, modules.<module>.pages.<page>.title for a page below it. */
  titleKey: MessageKey;
  /** Who the page is for; the shell shows it to them only. Hiding is a convenience: the backend's policies decide. */
  audience: Audience;
  component: Component;
}

/**
 * A page below a module that is reached from another page, not from the navigation: its address may hold parameters
 * (`versions/:id`), and it is listed neither in the sidebar nor in the palette. The breadcrumbs name it by its title.
 */
export interface ModuleDetail {
  /** Below the module, one or more segments; a segment `:name` is a parameter matching any one segment. */
  path: string;
  /** modules.<module>.details.<detail>.title. */
  titleKey: MessageKey;
  /** Who the page is for, whatever its module's audience; the shell shows it to them only. */
  audience: Audience;
  component: Component;
}

/** What src/modules/<module>/routes.ts exports as `route`; the app finds every module's file by itself (no list to edit). */
export interface ModuleRoute extends ModulePage {
  /** The module's pages below its own, at /<module>/<page>; each is governed by its own audience. */
  pages?: ModulePage[];
  /** The module's detail pages, at /<module>/<detail's path>; each is governed by its own audience. */
  details?: ModuleDetail[];
}

export const moduleRoutes = Object.values(
  import.meta.glob<ModuleRoute>('./*/routes.ts', { eager: true, import: 'route' }),
);
