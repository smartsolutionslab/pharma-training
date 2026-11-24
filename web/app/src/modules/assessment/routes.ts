import type { ModuleRoute } from '@/modules';
import AssessmentPage from './AssessmentPage.vue';
import ExaminationPage from './ExaminationPage.vue';
import ExaminationResultPage from './ExaminationResultPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'assessment',
  titleKey,
  audience: 'employee',
  component: AssessmentPage,
  details: [
    {
      path: 'sessions/:sessionId',
      titleKey: 'modules.assessment.details.test.title',
      audience: 'employee',
      component: ExaminationPage,
    },
    {
      path: 'sessions/:sessionId/result',
      titleKey: 'modules.assessment.details.result.title',
      audience: 'employee',
      component: ExaminationResultPage,
    },
  ],
};
