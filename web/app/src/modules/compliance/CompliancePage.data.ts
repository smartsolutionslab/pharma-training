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

const privacy = '0198c1a0-0000-7000-8000-0000000000c2';
const hygiene = '0198c1a0-0000-7000-8000-0000000000c1';
const emergency = '0198c1a0-0000-7000-8000-0000000000c3';

/** A cell of the matrix: the latest non-cancelled assignment of a course to a member. */
const cell = (
  courseId: string,
  number: number,
  state: string,
  dueDate: string,
  lastCompletedOn: string | null = null,
  validUntil: string | null = null,
) => ({
  courseId,
  assignmentId: `0198c1a0-0000-7000-8000-0000000000f${number}`,
  state,
  dueDate,
  lastCompletedOn,
  validUntil,
});

/**
 * The current pharmacy's matrix (GET /compliance/pharmacies/{id}/matrix): three courses by four members, every status
 * once (Carl's privacy completion ran out on 2026-11-22, so it shows Expired), an empty cell (Anna has no hygiene
 * course), and the emergency course with no rate, as under a job function none of whose members has it.
 */
const matrix = {
  courses: [
    {
      courseId: privacy,
      title: 'Datenschutz in der Apotheke',
      assignments: 4,
      compliantAssignments: 3,
      overdueAssignments: 0,
      complianceRate: 0.75,
    },
    {
      courseId: hygiene,
      title: 'Hygiene in der Apotheke',
      assignments: 3,
      compliantAssignments: 2,
      overdueAssignments: 1,
      complianceRate: 2 / 3,
    },
    {
      courseId: emergency,
      title: 'Notfallmanagement',
      assignments: 0,
      compliantAssignments: 0,
      overdueAssignments: 0,
      complianceRate: null,
    },
  ],
  members: {
    items: [
      {
        personId: '0198c1a0-0000-7000-8000-0000000000e1',
        name: 'Anna Albers',
        jobFunction: 'Pharmacist',
        cells: [cell(privacy, 1, 'InProgress', '2027-01-10')],
      },
      {
        personId: '0198c1a0-0000-7000-8000-0000000000e2',
        name: 'Berta Braun',
        jobFunction: 'Pka',
        cells: [
          cell(privacy, 2, 'Completed', '2026-06-30', '2026-06-01', '2027-06-01'),
          cell(hygiene, 3, 'Overdue', '2026-11-22'),
        ],
      },
      {
        personId: '0198c1a0-0000-7000-8000-0000000000e3',
        name: 'Carl Claasen',
        jobFunction: 'Pta',
        cells: [
          cell(privacy, 4, 'Completed', '2025-11-30', '2025-11-20', '2026-11-22'),
          cell(hygiene, 5, 'CompletedLate', '2026-02-01', '2026-02-10', '2027-02-10'),
        ],
      },
      {
        personId: '0198c1a0-0000-7000-8000-0000000000e4',
        name: 'Dora Dietz',
        jobFunction: 'Trainee',
        cells: [cell(privacy, 6, 'Open', '2027-03-15'), cell(hygiene, 7, 'Locked', '2026-12-31')],
      },
    ],
    totalCount: 4,
    page: 1,
    pageSize: 50,
  },
  assignments: 7,
  compliantAssignments: 5,
  overdueAssignments: 1,
  complianceRate: 5 / 7,
};

/** A pharmacy where no course is assigned yet: no columns, and no rate. */
const emptyMatrix = {
  courses: [],
  members: { items: [], totalCount: 0, page: 1, pageSize: 50 },
  assignments: 0,
  compliantAssignments: 0,
  overdueAssignments: 0,
  complianceRate: null,
};

export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetCompliancePharmaciesIdMatrix: matrix,
};

export const emptyExamples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetCompliancePharmaciesIdMatrix: emptyMatrix,
};

/** A course of the example and a job function, for the check with both filters chosen. */
export const filtered = { course: privacy, jobFunction: 'Pta' };
