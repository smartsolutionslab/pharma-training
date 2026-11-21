// The example data RegisterPharmacyPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way. The page posts a registration (a mutation, idle until the user submits) and reads the user's memberships
// only to choose the new pharmacy: one the user works in already, or none.
export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: {
    items: [
      {
        id: '0198c1a0-0000-7000-8000-0000000000a1',
        name: 'Apotheke am Markt',
        membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
        role: 'Employee',
        jobFunction: 'Pta',
        canManageMembers: false,
        canAssignCourses: false,
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 100,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: { items: [], totalCount: 0, page: 1, pageSize: 100 },
};
