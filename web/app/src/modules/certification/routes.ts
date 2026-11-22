import type { ModuleRoute } from '@/modules';
import CertificationPage from './CertificationPage.vue';
import { titleKey } from './titleKey';

export const route: ModuleRoute = {
  path: 'certification',
  titleKey,
  audience: 'employee',
  component: CertificationPage,
};
