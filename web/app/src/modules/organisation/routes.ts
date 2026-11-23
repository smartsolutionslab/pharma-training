import type { ModuleRoute } from '@/modules';
import AcceptInvitationPage from './AcceptInvitationPage.vue';
import { captureInvitationLink } from './invitationLink';
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
  entries: [
    // The invitation mail's link (ADR 0004), for every signed-in user, with or without a pharmacy: its guard takes the
    // token out of the address before anything else loads.
    {
      path: '/invitations/accept',
      titleKey: 'modules.organisation.entries.acceptInvitation.title',
      audience: 'signed-in',
      component: AcceptInvitationPage,
      beforeEnter: captureInvitationLink,
    },
  ],
};
