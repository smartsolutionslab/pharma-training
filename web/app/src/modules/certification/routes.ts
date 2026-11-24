import type { ModuleRoute } from '@/modules';
import CertificationPage from './CertificationPage.vue';
import ExportCertificatesPage from './ExportCertificatesPage.vue';
import { titleKey } from './titleKey';
import VerifyCertificatePage from './VerifyCertificatePage.vue';

export const route: ModuleRoute = {
  path: 'certification',
  titleKey,
  // A certificate is the person's, whatever their pharmacy (docs/domain/README.md, Z1): every signed-in user's page.
  audience: 'signed-in',
  component: CertificationPage,
  pages: [
    // The certificates of the current pharmacy's members as a ZIP (#297), for a member who may manage it; whether they
    // may export there the host decides (ADR 0003).
    {
      path: 'export',
      titleKey: 'modules.certification.pages.export.title',
      audience: 'manager',
      component: ExportCertificatesPage,
    },
  ],
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
