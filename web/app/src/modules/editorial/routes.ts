import type { ModuleRoute } from '@/modules';
import CourseTestPage from './CourseTestPage.vue';
import CourseVersionPage from './CourseVersionPage.vue';
import EditorialPage from './EditorialPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'editorial',
  titleKey,
  audience: 'editor',
  component: EditorialPage,
  details: [
    {
      path: 'versions/:id',
      titleKey: 'modules.editorial.details.version.title',
      audience: 'editor',
      component: CourseVersionPage,
    },
    {
      path: 'versions/:id/test',
      titleKey: 'modules.editorial.details.test.title',
      audience: 'editor',
      component: CourseTestPage,
    },
  ],
};
