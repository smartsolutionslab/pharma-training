import type { ModuleRoute } from '@/modules';
import AssessmentPage from './AssessmentPage.vue';

export const route: ModuleRoute = {
  path: 'assessment',
  titleKey: 'modules.assessment.title',
  audience: 'employee',
  component: AssessmentPage,
};
