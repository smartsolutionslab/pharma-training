import type { ModuleRoute } from '@/modules';
import CompliancePage from './CompliancePage.vue';

export const route: ModuleRoute = {
  path: 'compliance',
  titleKey: 'modules.compliance.title',
  audience: 'employee',
  component: CompliancePage,
};
