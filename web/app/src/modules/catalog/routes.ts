import type { ModuleRoute } from '@/modules';
import CatalogPage from './CatalogPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'catalog',
  titleKey,
  audience: 'manager',
  component: CatalogPage,
};
