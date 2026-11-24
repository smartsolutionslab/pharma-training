// The example data ExportCertificatesPage's axe check shows the page with, by hook of the generated client. A hook
// without an example answers as if its request were still on its way. The export itself is a plain call, no hook: the
// page holds no ZIP in the query cache.

/** The manager's membership (GET /organisation/pharmacies/mine): the page exports the current pharmacy's certificates. */
export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: {
    items: [
      {
        id: '0198c1a0-0000-7000-8000-0000000000a1',
        name: 'Apotheke am Markt',
        membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
        role: 'Manager',
        jobFunction: 'Pharmacist',
        canManageMembers: true,
        canAssignCourses: true,
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 100,
  },
};
