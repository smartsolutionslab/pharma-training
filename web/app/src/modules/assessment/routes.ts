import type { ModuleRoute } from '@/modules';
import AssessmentPage from './AssessmentPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'assessment',
  titleKey,
  audience: 'employee',
  component: AssessmentPage,
};
