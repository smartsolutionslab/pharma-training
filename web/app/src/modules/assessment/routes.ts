import type { ModuleRoute } from '@/modules';
import AssessmentPage from './AssessmentPage.vue';

export const route: ModuleRoute = {
  path: 'assessment',
  title: 'Assessment',
  audience: 'employee',
  component: AssessmentPage,
};
