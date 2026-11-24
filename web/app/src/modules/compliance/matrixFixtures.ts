import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render } from '@testing-library/vue';
import type userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import type { ComplianceAssignmentState, ComplianceJobFunction, MyPharmacyDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import CompliancePage from './CompliancePage.vue';

// The team matrix's tests (#36) share the host they talk to and the way to the page. The host answers what
// GET /organisation/pharmacies/mine and GET /compliance/pharmacies/{id}/matrix would, from the state a test gives it.
// The matrix's answer is written in the shape of #34's final contract (ComplianceMatrixDto in the generated client),
// with its counts as numbers.

export type User = ReturnType<typeof userEvent.setup>;
export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/** The day the tests run on, at 10:00 local time: today is 2026-11-23. */
export const now = new Date(2026, 10, 23, 10, 0, 0);

/** A manager of the Apotheke am Markt, who may assign courses there. */
export const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A manager of the same pharmacy who may manage its members but not assign courses: the shell lets them in. */
export const membersOnly: MyPharmacyDto = { ...markt, canAssignCourses: false };

/** A cell of the matrix: the latest non-cancelled assignment of a course to a member. */
export interface MatrixCell {
  courseId: string;
  assignmentId: string;
  state: ComplianceAssignmentState;
  dueDate: string;
  lastCompletedOn: string | null;
  validUntil: string | null;
}

/** The figures of a column, or of the whole matrix: over every row of the filter, not only one page. */
export interface MatrixTally {
  assignments: number;
  compliantAssignments: number;
  overdueAssignments: number;
  complianceRate: number | null;
}

export interface MatrixCourse extends MatrixTally {
  courseId: string;
  title: string;
}

export interface MatrixRow {
  personId: string;
  name: string;
  jobFunction: ComplianceJobFunction;
  cells: MatrixCell[];
}

/** GET /compliance/pharmacies/{id}/matrix's answer (ComplianceMatrixDto, as #34 rewrites it). */
export interface MatrixAnswer extends MatrixTally {
  courses: MatrixCourse[];
  members: { items: MatrixRow[]; totalCount: number; page: number; pageSize: number };
}

export const hygiene: MatrixCourse = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c1',
  title: 'Hygiene in der Apotheke',
  assignments: 2,
  compliantAssignments: 1,
  overdueAssignments: 1,
  complianceRate: 0.5,
};

export const privacy: MatrixCourse = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c2',
  title: 'Datenschutz in der Apotheke',
  assignments: 3,
  compliantAssignments: 2,
  overdueAssignments: 0,
  complianceRate: 2 / 3,
};

export const emergency: MatrixCourse = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c3',
  title: 'Notfallmanagement',
  assignments: 2,
  compliantAssignments: 2,
  overdueAssignments: 0,
  complianceRate: 1,
};

/** The team's columns, by title as the host sends them. */
export const teamCourses: MatrixCourse[] = [privacy, hygiene, emergency];

/** A pharmacist with an open and a started course, and none of the emergency course: an empty cell. */
export const anna: MatrixRow = {
  personId: '0198c1a0-0000-7000-8000-0000000000e1',
  name: 'Anna Albers',
  jobFunction: 'Pharmacist',
  cells: [
    {
      courseId: privacy.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f3',
      state: 'InProgress',
      dueDate: '2027-01-10',
      lastCompletedOn: null,
      validUntil: null,
    },
    {
      courseId: hygiene.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f1',
      state: 'Open',
      dueDate: '2027-03-15',
      lastCompletedOn: null,
      validUntil: null,
    },
  ],
};

/** A PKA with a valid completion, an overdue course and a locked one: three courses. */
export const berta: MatrixRow = {
  personId: '0198c1a0-0000-7000-8000-0000000000e2',
  name: 'Berta Braun',
  jobFunction: 'Pka',
  cells: [
    {
      courseId: privacy.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f4',
      state: 'Completed',
      dueDate: '2026-06-30',
      lastCompletedOn: '2026-06-01',
      validUntil: '2027-06-01',
    },
    {
      courseId: hygiene.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f2',
      state: 'Overdue',
      dueDate: '2026-11-22',
      lastCompletedOn: null,
      validUntil: null,
    },
    {
      courseId: emergency.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f5',
      state: 'Locked',
      dueDate: '2026-12-31',
      lastCompletedOn: null,
      validUntil: null,
    },
  ],
};

/** A PTA whose completion of the privacy course ran out yesterday (Expired today) and who completed one late. */
export const carl: MatrixRow = {
  personId: '0198c1a0-0000-7000-8000-0000000000e3',
  name: 'Carl Claasen',
  jobFunction: 'Pta',
  cells: [
    {
      courseId: privacy.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f6',
      state: 'Completed',
      dueDate: '2025-11-30',
      lastCompletedOn: '2025-11-20',
      validUntil: '2026-11-22',
    },
    {
      courseId: emergency.courseId,
      assignmentId: '0198c1a0-0000-7000-8000-0000000000f7',
      state: 'CompletedLate',
      dueDate: '2026-02-01',
      lastCompletedOn: '2026-02-10',
      validUntil: '2027-02-10',
    },
  ],
};

/** A trainee with no course assigned here. */
export const dora: MatrixRow = {
  personId: '0198c1a0-0000-7000-8000-0000000000e4',
  name: 'Dora Dietz',
  jobFunction: 'Trainee',
  cells: [],
};

/**
 * The team: three courses by four members, every status once (Carl's privacy course is Expired on 2026-11-23), an
 * empty cell, and a member without any course. Seven cells, five on track, one overdue.
 */
export function teamMatrix(): MatrixAnswer {
  return {
    courses: teamCourses,
    members: { items: [anna, berta, carl, dora], totalCount: 4, page: 1, pageSize: 50 },
    assignments: 7,
    compliantAssignments: 5,
    overdueAssignments: 1,
    complianceRate: 5 / 7,
  };
}

/** Members 01 to n, each with an open hygiene course, as the host pages them by the request's page and pageSize. */
export function pagedTeam(total: number) {
  const members: MatrixRow[] = Array.from({ length: total }, (_, index) => {
    const number = String(index + 1).padStart(2, '0');
    return {
      personId: `0198c1a0-0000-7000-8001-0000000000${number}`,
      name: `Mitglied ${number}`,
      jobFunction: 'Pta',
      cells: [
        {
          courseId: hygiene.courseId,
          assignmentId: `0198c1a0-0000-7000-8002-0000000000${number}`,
          state: 'Open',
          dueDate: '2027-03-15',
          lastCompletedOn: null,
          validUntil: null,
        },
      ],
    };
  });
  return async (_pharmacyId: string, url: URL) => {
    const page = Number(url.searchParams.get('page') ?? '1');
    const size = Number(url.searchParams.get('pageSize') ?? '50');
    return Response.json({
      ...matrixOf([], [hygiene]),
      members: { items: members.slice((page - 1) * size, page * size), totalCount: total, page, pageSize: size },
    } satisfies MatrixAnswer);
  };
}

/** A matrix of one course and two members, the first page of 50. */
export function matrixOf(rows: MatrixRow[] = [anna, berta], courses: MatrixCourse[] = [hygiene]): MatrixAnswer {
  return {
    courses,
    members: { items: rows, totalCount: rows.length, page: 1, pageSize: 50 },
    assignments: 2,
    compliantAssignments: 1,
    overdueAssignments: 1,
    complianceRate: 0.5,
  };
}

/** What the fake host answers; a test changes it before or while the page asks. */
export interface MatrixHost {
  memberships: MyPharmacyDto[];
  /** The matrix's answer for a pharmacy, given the request's address. */
  matrix: (pharmacyId: string, url: URL) => Promise<Response>;
}

export function matrixHost(): MatrixHost {
  return {
    memberships: [markt],
    matrix: async () => Response.json(matrixOf()),
  };
}

/** An answer that never comes: the page stays as it is while it waits. */
export const never = () => new Promise<Response>(() => undefined);

/** The host as the generated client reaches it under /api, answering from the state. */
export function answering(host: MatrixHost): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === '/api/organisation/pharmacies/mine') {
      return Response.json({
        items: host.memberships,
        totalCount: host.memberships.length,
        page: 1,
        pageSize: Number(url.searchParams.get('pageSize') ?? '100'),
      });
    }
    const matrix = /^\/api\/compliance\/pharmacies\/([0-9a-f-]+)\/matrix$/.exec(url.pathname);
    if (method === 'GET' && matrix?.[1] !== undefined) {
      return host.matrix(matrix[1], url);
    }
    return new Response('', { status: 404 });
  };
}

/** Problem details as the host sends a module's refusal, or the framework's without a code. */
export function problem(status: number, code?: string, detail = 'Words of the host, not for the user.'): Response {
  return new Response(JSON.stringify({ title: 'Refused', status, detail, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

/** The GET requests the host received at a path, by their query. */
export function readsOf(host: { mock: { calls: Parameters<Fetch>[] } }, pathname: string) {
  return host.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') === 'GET')
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === pathname)
    .map((url) => Object.fromEntries(url.searchParams));
}

export const matrixPath = (pharmacy: MyPharmacyDto = markt) => `/api/compliance/pharmacies/${pharmacy.id}/matrix`;
export const minePath = '/api/organisation/pharmacies/mine';

/**
 * The page at an address (/compliance unless a test says otherwise), reached from another if a test says so, with a Pinia and a query client of its own that
 * does not retry, so a failure shows at once. The page reads the current pharmacy from the memberships, as the shell
 * does.
 */
export async function renderMatrixPage(options: { address?: string; from?: string; pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  if (options.from !== undefined) {
    await router.push(options.from);
  }
  await router.push(options.address ?? '/compliance');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(CompliancePage, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, pinia };
}
