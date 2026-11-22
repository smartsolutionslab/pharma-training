import type { ModuleRoute } from '@/modules';
import LearningPage from './LearningPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'learning',
  titleKey,
  audience: 'employee',
  component: LearningPage,
};
