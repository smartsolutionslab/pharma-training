import { getCertificationCertificatesIdPdf } from '@/api/generated';

/**
 * How the tab goes to a document: the seam a test watches instead of leaving its page. Nothing else navigates through
 * it.
 */
export const documentNavigation = {
  open(url: string) {
    window.location.assign(url);
  },
};

/**
 * Opens a certificate's PDF in this tab (#296, Z1), where the browser shows it inline, as #54 decided: the host signs a
 * five-minute read URL on the stored document for its holder (the generated client sends the user's token), and the
 * tab goes there; the bytes come from storage, never through the app. Rejects with the host's ApiError, and the tab
 * stays then.
 */
export async function openCertificate(certificateId: string): Promise<void> {
  const { url } = await getCertificationCertificatesIdPdf(certificateId);
  documentNavigation.open(url);
}
