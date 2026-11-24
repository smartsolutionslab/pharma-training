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

/** The schemes the tab follows: a web address, never a script, a data URL or a file. */
const followed = new Set(['https:', 'http:']);

/** Whether the URL is a web address the tab may go to; one that is no URL at all is not. */
function isWebAddress(url: string): boolean {
  try {
    return followed.has(new URL(url).protocol);
  } catch {
    return false;
  }
}

/**
 * Opens a certificate's PDF in this tab (#296, Z1), where the browser shows it inline, as #54 decided: the host signs a
 * five-minute read URL on the stored document for its holder (the generated client sends the user's token), and the
 * tab goes there; the bytes come from storage, never through the app. Rejects with the host's ApiError, and the tab
 * stays then; it stays too, rejecting, when the host answers anything but an http(s) URL.
 */
export async function openCertificate(certificateId: string): Promise<void> {
  const { url } = await getCertificationCertificatesIdPdf(certificateId);
  if (!isWebAddress(url)) {
    throw new Error('The host signed no http(s) URL for the certificate.');
  }
  documentNavigation.open(url);
}
