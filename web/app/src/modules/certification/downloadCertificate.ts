import { type CertificationCertificateDto, getCertificationCertificatesIdPdf } from '@/api/generated';
import { certificateFileName } from './certificateFileName';

/** Storage did not serve the PDF at the URL the host signed: status is its HTTP status. */
export class DocumentReadFailed extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`Storage answered ${status}.`);
    this.name = 'DocumentReadFailed';
    this.status = status;
  }
}

/**
 * Saves a certificate's PDF under the course's name (#296, Z1). The host signs a five-minute read URL on the stored
 * document for its holder (the generated client sends the user's token); the bytes come from storage at that URL,
 * which carries its own authorisation, so no token goes there. Read as a blob, since a browser ignores a link's
 * download name on another origin, and saved through a link of the page's own. Rejects with the host's ApiError or
 * DocumentReadFailed, and saves nothing then.
 */
export async function downloadCertificate(
  certificate: Pick<CertificationCertificateDto, 'certificateId' | 'courseTitle'>,
): Promise<void> {
  const { url } = await getCertificationCertificatesIdPdf(certificate.certificateId);
  const response = await fetch(url);
  if (!response.ok) {
    throw new DocumentReadFailed(response.status);
  }
  saveBlob(await response.blob(), certificateFileName(certificate.courseTitle));
}

function saveBlob(blob: Blob, name: string) {
  const href = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = href;
  link.download = name;
  link.hidden = true;
  document.body.append(link);
  try {
    link.click();
  } finally {
    link.remove();
    // Once the click has handed the blob to the browser's download.
    setTimeout(() => URL.revokeObjectURL(href), 0);
  }
}
