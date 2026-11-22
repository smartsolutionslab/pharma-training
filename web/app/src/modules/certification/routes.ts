import type { ModuleRoute } from '@/modules';
import CertificationPage from './CertificationPage.vue';

export const route: ModuleRoute = {
  path: 'certification',
  titleKey: 'modules.certification.title',
  audience: 'employee',
  component: CertificationPage,
};
