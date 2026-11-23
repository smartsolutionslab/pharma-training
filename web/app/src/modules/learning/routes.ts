import type { ModuleRoute } from '@/modules';
import CoursePlayerPage from './CoursePlayerPage.vue';
import LearningPage from './LearningPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'learning',
  titleKey,
  audience: 'employee',
  component: LearningPage,
  details: [
    {
      path: 'courses/:courseVersionId',
      titleKey: 'modules.learning.details.player.title',
      audience: 'employee',
      component: CoursePlayerPage,
    },
  ],
};
