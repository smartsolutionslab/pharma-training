import type { ModuleRoute } from '@/modules';
import CompliancePage from './CompliancePage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'compliance',
  titleKey,
  audience: 'employee',
  component: CompliancePage,
};
