import type { ModuleRoute } from '@/modules';
import CatalogPage from './CatalogPage.vue';

export const route: ModuleRoute = {
  path: 'catalog',
  titleKey: 'modules.catalog.title',
  audience: 'manager',
  component: CatalogPage,
};
