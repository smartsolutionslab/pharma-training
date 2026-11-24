import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render } from '@testing-library/vue';
import type userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import type { ComplianceMyAssignmentDto, MyPharmacyDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import LearningPage from './LearningPage.vue';

// My courses' tests (#38) share the host they talk to and the way to the page. The host answers what
// GET /organisation/pharmacies/mine and GET /compliance/me/assignments would, from the state a test gives it, in the
// shape of #35's contract (PagedResultOfComplianceMyAssignmentDto in the generated client), with its counts as numbers.
// The host decides the order (overdue first, then by due date, then by title); the answers here are in that order.

export type User = ReturnType<typeof userEvent.setup>;
export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/** An employee of the Apotheke am Markt, who may not assign courses there. */
export const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};

/** A manager of the same pharmacy, who may assign courses there. */
export const marktManager: MyPharmacyDto = {
  ...markt,
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A second pharmacy the caller works in, as an employee. */
export const ufer: MyPharmacyDto = {
  ...markt,
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Ufer',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
};

/** Overdue since 2026-11-20, never started: Start opens the current version. */
export const overdue: ComplianceMyAssignmentDto = {
  assignmentId: '0198c1a0-0000-7000-8000-0000000000d1',
  courseId: '0198c1a0-0000-7000-8000-0000000000c1',
  courseTitle: 'Hygiene in der Apotheke',
  state: 'Overdue',
  dueDate: '2026-11-20',
  startCourseVersionId: '0198c1a0-0000-7000-8000-0000000000e1',
  continuesSession: false,
};

/** Started, with the test locked after failed attempts: Continue resumes the running session's version. */
export const locked: ComplianceMyAssignmentDto = {
  assignmentId: '0198c1a0-0000-7000-8000-0000000000d2',
  courseId: '0198c1a0-0000-7000-8000-0000000000c2',
  courseTitle: 'Datenschutz',
  state: 'Locked',
  dueDate: '2026-12-01',
  startCourseVersionId: '0198c1a0-0000-7000-8000-0000000000e2',
  continuesSession: true,
};

/** Started: Continue resumes the running session's version. */
export const inProgress: ComplianceMyAssignmentDto = {
  assignmentId: '0198c1a0-0000-7000-8000-0000000000d3',
  courseId: '0198c1a0-0000-7000-8000-0000000000c3',
  courseTitle: 'Notfallmanagement',
  state: 'InProgress',
  dueDate: '2026-12-15',
  startCourseVersionId: '0198c1a0-0000-7000-8000-0000000000e3',
  continuesSession: true,
};

/** Not started yet: Start opens the current version. */
export const open: ComplianceMyAssignmentDto = {
  assignmentId: '0198c1a0-0000-7000-8000-0000000000d4',
  courseId: '0198c1a0-0000-7000-8000-0000000000c4',
  courseTitle: 'Arzneimittelsicherheit',
  state: 'Open',
  dueDate: '2027-01-10',
  startCourseVersionId: '0198c1a0-0000-7000-8000-0000000000e4',
  continuesSession: false,
};

/** Not started, and its latest version is retired: there is nothing to open. */
export const retired: ComplianceMyAssignmentDto = {
  assignmentId: '0198c1a0-0000-7000-8000-0000000000d5',
  courseId: '0198c1a0-0000-7000-8000-0000000000c5',
  courseTitle: 'Betäubungsmittel',
  state: 'Open',
  dueDate: '2027-02-01',
  startCourseVersionId: null,
  continuesSession: false,
};

/** The learner's courses, in the host's order. */
export const myCourses: ComplianceMyAssignmentDto[] = [overdue, locked, inProgress, open, retired];

/** GET /compliance/me/assignments's answer (PagedResultOfComplianceMyAssignmentDto). */
export interface AssignmentsAnswer {
  items: ComplianceMyAssignmentDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** One page of 50 holding these courses, in the order given. */
export function assignmentsOf(items: ComplianceMyAssignmentDto[] = myCourses): AssignmentsAnswer {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

/** Courses 01 to n, each open, as the host pages them by the request's page and pageSize. */
export function pagedCourses(total: number) {
  const courses: ComplianceMyAssignmentDto[] = Array.from({ length: total }, (_, index) => {
    const number = String(index + 1).padStart(2, '0');
    return {
      assignmentId: `0198c1a0-0000-7000-8001-0000000000${number}`,
      courseId: `0198c1a0-0000-7000-8002-0000000000${number}`,
      courseTitle: `Schulung ${number}`,
      state: 'Open',
      dueDate: '2027-03-15',
      startCourseVersionId: `0198c1a0-0000-7000-8003-0000000000${number}`,
      continuesSession: false,
    };
  });
  return async (_pharmacyId: string, url: URL) => {
    const page = Number(url.searchParams.get('page') ?? '1');
    const size = Number(url.searchParams.get('pageSize') ?? '50');
    return Response.json({
      items: courses.slice((page - 1) * size, page * size),
      totalCount: total,
      page,
      pageSize: size,
    } satisfies AssignmentsAnswer);
  };
}

/** What the fake host answers; a test changes it before or while the page asks. */
export interface MyCoursesHost {
  memberships: MyPharmacyDto[];
  /** The memberships' answer; by default the memberships above, at once. */
  mine: (host: MyCoursesHost, url: URL) => Promise<Response>;
  /** The assignments' answer for the pharmacy the request names, given the request's address. */
  assignments: (pharmacyId: string, url: URL) => Promise<Response>;
}

/** The memberships as the host lists them. */
export function membershipsAnswer(memberships: MyPharmacyDto[], pageSize = 100): Response {
  return Response.json({ items: memberships, totalCount: memberships.length, page: 1, pageSize });
}

export function myCoursesHost(): MyCoursesHost {
  return {
    memberships: [markt],
    mine: async (host, url) => membershipsAnswer(host.memberships, Number(url.searchParams.get('pageSize') ?? '100')),
    assignments: async () => Response.json(assignmentsOf()),
  };
}

/** An answer that never comes: the page stays as it is while it waits. */
export const never = () => new Promise<Response>(() => undefined);

export const minePath = '/api/organisation/pharmacies/mine';
export const assignmentsPath = '/api/compliance/me/assignments';

/** The host as the generated client reaches it under /api, answering from the state. */
export function answering(host: MyCoursesHost): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === minePath) {
      return host.mine(host, url);
    }
    if (method === 'GET' && url.pathname === assignmentsPath) {
      return host.assignments(url.searchParams.get('pharmacyId') ?? '', url);
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

/** The request the page sends for a page of a pharmacy's courses: always the pharmacy, the page and 50 a page. */
export const readOf = (page = 1, pharmacy: MyPharmacyDto = markt) => ({
  pharmacyId: pharmacy.id,
  page: String(page),
  pageSize: '50',
});

/**
 * The page at an address (/learning unless a test says otherwise), reached from another if a test says so, with a
 * Pinia and a query client of its own that does not retry, so a failure shows at once. The page reads the current
 * pharmacy from the memberships, as the shell does.
 */
export async function renderMyCoursesPage(options: { address?: string; from?: string; pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  if (options.from !== undefined) {
    await router.push(options.from);
  }
  await router.push(options.address ?? '/learning');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(LearningPage, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, pinia };
}
