import type { ModuleRoute } from '@/modules';
import EditorialPage from './EditorialPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'editorial',
  titleKey,
  audience: 'editor',
  component: EditorialPage,
};
