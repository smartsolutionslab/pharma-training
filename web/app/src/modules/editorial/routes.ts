import type { ModuleRoute } from '@/modules';
import EditorialPage from './EditorialPage.vue';

export const route: ModuleRoute = {
  path: 'editorial',
  titleKey: 'modules.editorial.title',
  audience: 'editor',
  component: EditorialPage,
};
