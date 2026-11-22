import type { ModuleRoute } from '@/modules';
import LearningPage from './LearningPage.vue';

export const route: ModuleRoute = {
  path: 'learning',
  titleKey: 'modules.learning.title',
  audience: 'employee',
  component: LearningPage,
};
