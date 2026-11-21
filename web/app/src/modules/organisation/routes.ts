import type { ModuleRoute } from '@/modules';
import OrganisationPage from './OrganisationPage.vue';

export const route: ModuleRoute = {
  path: 'organisation',
  title: 'Organisation',
  audience: 'manager',
  component: OrganisationPage,
};
