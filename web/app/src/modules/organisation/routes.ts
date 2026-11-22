import type { ModuleRoute } from '@/modules';
import OrganisationPage from './OrganisationPage.vue';
import { titleKey } from './titleKey';
import RegisterPharmacyPage from './RegisterPharmacyPage.vue';

export const route: ModuleRoute = {
  path: 'organisation',
  titleKey,
  audience: 'manager',
  component: OrganisationPage,
  pages: [
    // Registering a pharmacy needs none: every signed-in user may found one (POST /organisation/pharmacies).
    {
      path: 'register',
      titleKey: 'modules.organisation.pages.register.title',
      audience: 'signed-in',
      component: RegisterPharmacyPage,
    },
  ],
};
