// The example data AssignCoursePage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

/** The manager's membership (GET /organisation/pharmacies/mine): the page assigns in the current pharmacy. */
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

// The catalog (GET /catalog/courses), the last page of a longer list so the pager shows, and the pharmacy's members
// (GET /compliance/pharmacies/{id}/members), one of each job function but one.
export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetCatalogCourses: {
    items: [
      {
        courseId: '0198c1a0-0000-7000-8000-0000000000c1',
        courseVersionId: '0198c1a0-0000-7000-8000-0000000000d1',
        versionNumber: 2,
        title: 'Hygiene in der Apotheke',
        validityMonths: 12,
      },
      {
        courseId: '0198c1a0-0000-7000-8000-0000000000c2',
        courseVersionId: '0198c1a0-0000-7000-8000-0000000000d2',
        versionNumber: 1,
        title: 'Kennzeichnung von Rezepturen',
        validityMonths: 24,
      },
    ],
    totalCount: 22,
    page: 2,
    pageSize: 20,
  },
  useGetCompliancePharmaciesIdMembers: {
    items: [
      { personId: '0198c1a0-0000-7000-8000-0000000000e1', name: 'Anna Albers', jobFunction: 'Pharmacist' },
      { personId: '0198c1a0-0000-7000-8000-0000000000e2', name: 'Ben Becker', jobFunction: 'Pta' },
      { personId: '0198c1a0-0000-7000-8000-0000000000e3', name: 'Carla Claasen', jobFunction: 'Pka' },
      { personId: '0198c1a0-0000-7000-8000-0000000000e4', name: 'David Dahl', jobFunction: 'Trainee' },
    ],
    totalCount: 4,
    page: 1,
    pageSize: 20,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetCatalogCourses: { items: [], totalCount: 0, page: 1, pageSize: 20 },
  useGetCompliancePharmaciesIdMembers: { items: [], totalCount: 0, page: 1, pageSize: 20 },
};
