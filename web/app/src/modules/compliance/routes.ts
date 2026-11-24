import type { ModuleRoute } from '@/modules';
import AssignCoursePage from './AssignCoursePage.vue';
import CompliancePage from './CompliancePage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'compliance',
  titleKey,
  // The team matrix (#36), the manager's main page; whether the member may view it the host decides.
  audience: 'manager',
  component: CompliancePage,
  pages: [
    // Assigning a course needs a pharmacy the user may manage; whether they may assign there the host decides (#37).
    {
      path: 'assign',
      titleKey: 'modules.compliance.pages.assign.title',
      audience: 'manager',
      component: AssignCoursePage,
    },
  ],
};
