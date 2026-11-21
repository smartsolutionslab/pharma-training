import type { ModuleRoute } from '@/modules';
import LearningPage from './LearningPage.vue';

export const route: ModuleRoute = {
  path: 'learning',
  title: 'Learning',
  audience: 'employee',
  component: LearningPage,
};
