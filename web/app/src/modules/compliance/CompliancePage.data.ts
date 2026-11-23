// The example data CompliancePage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

/** The manager's membership (GET /organisation/pharmacies/mine): the matrix is the current pharmacy's. */
const memberships = {
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
};

// The membership alone, so the page is checked with its matrix still on its way; the matrix's examples follow with its
// table (#36, T3).
export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
};

export const emptyExamples: Record<string, unknown> = {};
