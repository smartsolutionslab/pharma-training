import type { ModuleRoute } from '@/modules';
import CertificationPage from './CertificationPage.vue';

export const route: ModuleRoute = {
  path: 'certification',
  title: 'Certification',
  audience: 'employee',
  component: CertificationPage,
};
