// The example data CertificationPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way. One certificate's PDF is ready, the other's is still being prepared.
export const examples: Record<string, unknown> = {
  useGetCertificationMeCertificates: {
    items: [
      {
        certificateId: '0198c1a0-0000-7000-8000-0000000000e1',
        courseTitle: 'Hygiene in der Apotheke',
        issuedOn: '2026-01-15',
        validUntil: '2027-01-15',
        validity: 'Valid',
        documentReady: true,
      },
      {
        certificateId: '0198c1a0-0000-7000-8000-0000000000e2',
        courseTitle: 'Datenschutz',
        issuedOn: '2024-03-02',
        validUntil: '2025-03-02',
        validity: 'Expired',
        documentReady: false,
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 20,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetCertificationMeCertificates: { items: [], totalCount: 0, page: 1, pageSize: 20 },
};
