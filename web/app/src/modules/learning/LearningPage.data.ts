// The example data LearningPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

/** The employee's membership (GET /organisation/pharmacies/mine): the courses are the current pharmacy's. */
const memberships = {
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
};

/** One of the caller's active assignments, as GET /compliance/me/assignments names it. */
const assignment = (
  number: number,
  courseTitle: string,
  state: string,
  dueDate: string,
  startCourseVersionId: string | null,
  continuesSession: boolean,
) => ({
  assignmentId: `0198c1a0-0000-7000-8000-0000000000d${number}`,
  courseId: `0198c1a0-0000-7000-8000-0000000000c${number}`,
  courseTitle,
  state,
  dueDate,
  startCourseVersionId,
  continuesSession,
});

/**
 * The caller's courses in the host's order: overdue first, then by due date. Every state the endpoint sends, a course
 * to continue, and one whose version is retired, so it cannot be started.
 */
const assignments = {
  items: [
    assignment(1, 'Hygiene in der Apotheke', 'Overdue', '2026-11-20', '0198c1a0-0000-7000-8000-0000000000e1', false),
    assignment(2, 'Datenschutz', 'Locked', '2026-12-01', '0198c1a0-0000-7000-8000-0000000000e2', true),
    assignment(3, 'Notfallmanagement', 'InProgress', '2026-12-15', '0198c1a0-0000-7000-8000-0000000000e3', true),
    assignment(4, 'Arzneimittelsicherheit', 'Open', '2027-01-10', '0198c1a0-0000-7000-8000-0000000000e4', false),
    assignment(5, 'Betäubungsmittel', 'Open', '2027-02-01', null, false),
  ],
  totalCount: 5,
  page: 1,
  pageSize: 50,
};

export const examples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetComplianceMeAssignments: assignments,
};

export const emptyExamples: Record<string, unknown> = {
  useGetOrganisationPharmaciesMine: memberships,
  useGetComplianceMeAssignments: { items: [], totalCount: 0, page: 1, pageSize: 50 },
};
