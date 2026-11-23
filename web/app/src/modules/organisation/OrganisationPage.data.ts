// The example data OrganisationPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

/** The manager's membership (GET /organisation/pharmacies/mine): the page manages the current pharmacy's members. */
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

// The pharmacy's active members (GET /organisation/pharmacies/{id}/members), the manager's own row among them, and its
// invitations (GET …/invitations), one open and one long expired, whatever day the check runs.
export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetOrganisationPharmaciesIdMembers: {
    items: [
      {
        membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
        personId: '0198c1a0-0000-7000-8000-0000000000e1',
        name: 'Anna Albers',
        jobFunction: 'Pta',
        role: 'Employee',
        since: '2026-11-01T10:00:00Z',
      },
      {
        membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
        personId: '0198c1a0-0000-7000-8000-0000000000e0',
        name: 'Local Developer',
        jobFunction: 'Pharmacist',
        role: 'Manager',
        since: '2026-11-22T10:00:00Z',
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 100,
  },
  useGetOrganisationPharmaciesIdInvitations: {
    items: [
      {
        membershipId: '0198c1a0-0000-7000-8000-0000000000b4',
        email: 'carla@example.com',
        jobFunction: 'Trainee',
        role: 'Employee',
        expiresAt: '2000-01-01T12:00:00Z',
      },
      {
        membershipId: '0198c1a0-0000-7000-8000-0000000000b3',
        email: 'ben@example.com',
        jobFunction: 'Pka',
        role: 'Employee',
        expiresAt: '2999-12-01T12:00:00Z',
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 100,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetOrganisationPharmaciesIdMembers: { items: [], totalCount: 0, page: 1, pageSize: 100 },
  useGetOrganisationPharmaciesIdInvitations: { items: [], totalCount: 0, page: 1, pageSize: 100 },
};
