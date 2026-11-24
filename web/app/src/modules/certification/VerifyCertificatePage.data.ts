import type { CertificateVerificationDto } from '@/api/generated';

// The example data VerifyCertificatePage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way. A verification has no empty answer: the other example is an expired certificate, the page's other result.

const valid: CertificateVerificationDto = {
  holderName: 'Anna Schmidt',
  courseTitle: 'Hygiene in der Apotheke',
  issuedOn: '2026-11-24',
  validUntil: '2027-11-24',
  validity: 'Valid',
};

const expired: CertificateVerificationDto = {
  holderName: 'Berta Bauer',
  courseTitle: 'Datenschutz',
  issuedOn: '2024-11-24',
  validUntil: '2025-11-24',
  validity: 'Expired',
};

export const examples: Record<string, unknown> = { useGetCertificationVerifyCode: valid };

export const emptyExamples: Record<string, unknown> = { useGetCertificationVerifyCode: expired };
