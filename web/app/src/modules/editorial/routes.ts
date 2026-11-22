import type { ModuleRoute } from '@/modules';
import CourseVersionPlaceholder from './CourseVersionPlaceholder.vue';
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
      component: CourseVersionPlaceholder,
    },
  ],
};
