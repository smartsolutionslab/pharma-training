import type { ModuleRoute } from '@/modules';
import EditorialPage from './EditorialPage.vue';

export const route: ModuleRoute = {
  path: 'editorial',
  title: 'Editorial',
  audience: 'editor',
  component: EditorialPage,
};
