import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { fireEvent, render, screen } from '@testing-library/vue';
import type userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import type { CatalogCourseDto, ComplianceJobFunction, ComplianceMemberDto, MyPharmacyDto } from '@/api/generated';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import AssignCoursePage from './AssignCoursePage.vue';

// The assignment wizard's tests (#37) share the host they talk to and the steps through the wizard. The host answers
// what GET /organisation/pharmacies/mine, GET /catalog/courses, GET /compliance/pharmacies/{id}/members and
// POST /compliance/pharmacies/{id}/assignments would, from the state a test gives it.

export type User = ReturnType<typeof userEvent.setup>;
export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/** The day the tests run on, at 10:00 local time: today is 2026-11-23. */
export const now = new Date(2026, 10, 23, 10, 0, 0);
export const today = '2026-11-23';

/** A manager who may assign courses in the Apotheke am Markt. */
export const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A second pharmacy the same manager may assign courses in. */
export const dom: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Dom',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A manager of the Apotheke am Markt who may manage its members but not assign courses: the shell lets them in. */
export const membersOnly: MyPharmacyDto = { ...markt, canAssignCourses: false };

export const hygiene: CatalogCourseDto = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c1',
  courseVersionId: '0198c1a0-0000-7000-8000-0000000000d1',
  versionNumber: 2,
  title: 'Hygiene in der Apotheke',
  validityMonths: 12,
};

export const labelling: CatalogCourseDto = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c2',
  courseVersionId: '0198c1a0-0000-7000-8000-0000000000d2',
  versionNumber: 1,
  title: 'Kennzeichnung von Rezepturen',
  validityMonths: 24,
};

export const anna: ComplianceMemberDto = {
  personId: '0198c1a0-0000-7000-8000-0000000000e1',
  name: 'Anna Albers',
  jobFunction: 'Pharmacist',
};
export const ben: ComplianceMemberDto = {
  personId: '0198c1a0-0000-7000-8000-0000000000e2',
  name: 'Ben Becker',
  jobFunction: 'Pta',
};
export const carla: ComplianceMemberDto = {
  personId: '0198c1a0-0000-7000-8000-0000000000e3',
  name: 'Carla Claasen',
  jobFunction: 'Pta',
};
export const david: ComplianceMemberDto = {
  personId: '0198c1a0-0000-7000-8000-0000000000e4',
  name: 'David Dahl',
  jobFunction: 'Pka',
};

/** The Apotheke am Markt's members, by name, as the host lists them. */
export const team = [anna, ben, carla, david];

/** As many members with the job function, named "<prefix> 001" onwards, with ids of their own. */
const jobFunctions: ComplianceJobFunction[] = ['Pharmacist', 'Pta', 'Pka', 'Trainee', 'Other'];

export function staff(count: number, jobFunction: ComplianceJobFunction, prefix = 'Mitglied'): ComplianceMemberDto[] {
  const group = jobFunctions.indexOf(jobFunction) + 1;
  return Array.from({ length: count }, (_, index) => {
    const number = String(index + 1).padStart(3, '0');
    return {
      personId: `0198c1a0-0000-7000-8000-f${group}${String(index + 1).padStart(10, '0')}`,
      name: `${prefix} ${number}`,
      jobFunction,
    };
  });
}

/** What the fake host answers; a test changes it before or while the page asks. */
export interface HostState {
  memberships: MyPharmacyDto[];
  courses: CatalogCourseDto[];
  /** The catalog's answer when it is not a page of the courses: a failure, say. */
  catalog?: () => Promise<Response>;
  /** Each pharmacy's active members, by name. */
  members: Record<string, ComplianceMemberDto[]>;
  /** The answer to POST /compliance/pharmacies/{id}/assignments, given the body. */
  assignment: (body: unknown) => Promise<Response>;
}

/** Every person posted is assigned, nobody refused. */
export const assignedAll = async (body: unknown) => {
  const { personIds } = body as { personIds: string[] };
  return Response.json({
    assigned: personIds.map((personId, index) => ({
      personId,
      assignmentId: `0198c1a0-0000-7000-8000-${String(index + 1).padStart(12, '0')}`,
    })),
    refused: [],
  });
};

export function hostState(): HostState {
  return {
    memberships: [markt],
    courses: [hygiene, labelling],
    members: { [markt.id]: team, [dom.id]: [] },
    assignment: assignedAll,
  };
}

function page<T>(items: T[], url: URL) {
  const pageNumber = Number(url.searchParams.get('page') ?? '1');
  const pageSize = Number(url.searchParams.get('pageSize') ?? '20');
  const start = (pageNumber - 1) * pageSize;
  return Response.json({
    items: items.slice(start, start + pageSize),
    totalCount: items.length,
    page: pageNumber,
    pageSize,
  });
}

/** The host as the generated client reaches it under /api, answering from the state. */
export function hostFrom(state: HostState): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === '/api/organisation/pharmacies/mine') {
      return page(state.memberships, url);
    }
    if (method === 'GET' && url.pathname === '/api/catalog/courses') {
      return state.catalog === undefined ? page(state.courses, url) : state.catalog();
    }
    const members = /^\/api\/compliance\/pharmacies\/([0-9a-f-]+)\/members$/.exec(url.pathname);
    if (method === 'GET' && members?.[1] !== undefined) {
      const jobFunction = url.searchParams.get('jobFunction');
      const all = state.members[members[1]] ?? [];
      return page(jobFunction === null ? all : all.filter((member) => member.jobFunction === jobFunction), url);
    }
    const assignments = /^\/api\/compliance\/pharmacies\/([0-9a-f-]+)\/assignments$/.exec(url.pathname);
    if (method === 'POST' && assignments?.[1] !== undefined) {
      return state.assignment(JSON.parse(String(init?.body)) as unknown);
    }
    return new Response('', { status: 404 });
  };
}

/** Problem details as the host sends a module's refusal (or the framework's, for already-exists). */
export function problem(status: number, code?: string, detail?: string): Response {
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

/** The assignments the host received, with their address and body. */
export function postsOf(host: { mock: { calls: Parameters<Fetch>[] } }) {
  return host.mock.calls
    .filter(([, init]) => init?.method === 'POST')
    .map(([input, init]) => ({ url: String(input), body: JSON.parse(String(init?.body)) as unknown }));
}

export const membersPath = (pharmacy: MyPharmacyDto = markt) => `/api/compliance/pharmacies/${pharmacy.id}/members`;
export const catalogPath = '/api/catalog/courses';

/**
 * The page at /compliance/assign with a Pinia and a query client of its own that does not retry, so a failure shows at
 * once. The page reads the current pharmacy from the memberships, as the shell does.
 */
export async function renderAssignPage(options: { pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/compliance/assign');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(AssignCoursePage, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, pinia };
}

/** A step's heading, once the step shows. */
export const stepHeading = (key: string) => screen.findByRole('heading', { level: 2, name: t(key) });

export const nextButton = () => screen.getByRole('button', { name: t('compliance.assign.next') });
export const backButton = () => screen.getByRole('button', { name: t('compliance.assign.back') });
export const submitButton = () => screen.getByRole('button', { name: t('compliance.assign.submit') });

export const chooseButton = (course: CatalogCourseDto) =>
  screen.findByRole('button', { name: t('compliance.assign.course.choose', { title: course.title }) });
export const memberCheckbox = (member: ComplianceMemberDto) =>
  screen.findByRole('checkbox', { name: t('compliance.assign.learners.select', { name: member.name }) });
export const jobFunctionButton = (jobFunction: ComplianceJobFunction) =>
  screen.getByRole('button', {
    name: t('compliance.assign.learners.selectJobFunction', { jobFunction: t(`glossary.jobFunction.${jobFunction}`) }),
  });
export const dueDateField = () => screen.getByLabelText(t('compliance.assign.due.date'));
export const repeatYearly = () => screen.getByRole('checkbox', { name: t('compliance.assign.due.repeat') });

/** The live count of the people selected, as the page words it. */
export const selectedCount = (count: number) => t('compliance.assign.learners.count', { count }, count);

/** Chooses the course and goes on to the learners. */
export async function toLearners(user: User, course: CatalogCourseDto = hygiene) {
  await user.click(await chooseButton(course));
  await user.click(nextButton());
  await stepHeading('compliance.assign.learners.title');
}

/** Selects the members one by one and goes on to the due date. */
export async function toDueDate(user: User, members: ComplianceMemberDto[] = [anna, ben], course = hygiene) {
  await toLearners(user, course);
  for (const member of members) {
    await user.click(await memberCheckbox(member));
  }
  await user.click(nextButton());
  await stepHeading('compliance.assign.due.title');
}

/** Sets the due date in the date field, as a browser's date picker does. */
export async function setDueDate(date: string) {
  await fireEvent.update(dueDateField(), date);
}

/** Goes through every step to the review, with a due date and the recurrence. */
export async function toReview(
  user: User,
  { members = [anna, ben], due = '2027-03-15', yearly = true, course = hygiene } = {},
) {
  await toDueDate(user, members, course);
  await setDueDate(due);
  if (yearly) {
    await user.click(repeatYearly());
  }
  await user.click(nextButton());
  await stepHeading('compliance.assign.review.title');
}
