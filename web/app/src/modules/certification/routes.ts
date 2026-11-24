import type { ModuleRoute } from '@/modules';
import CertificationPage from './CertificationPage.vue';
import { titleKey } from './titleKey';
import VerifyCertificatePage from './VerifyCertificatePage.vue';

export const route: ModuleRoute = {
  path: 'certification',
  titleKey,
  audience: 'employee',
  component: CertificationPage,
  entries: [
    // The link a certificate's QR code opens (#54, Z2), for anyone, signed in or not: outside the shell, without a
    // sign-in.
    {
      path: '/verify/:code',
      titleKey: 'modules.certification.entries.verify.title',
      audience: 'public',
      component: VerifyCertificatePage,
    },
  ],
};
