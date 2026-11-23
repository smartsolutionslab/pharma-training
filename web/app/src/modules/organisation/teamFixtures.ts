import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, within } from '@testing-library/vue';
import type userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PharmacyInvitationDto, PharmacyMemberDto } from '@/api/generated';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import OrganisationPage from './OrganisationPage.vue';

// The members and invitations page's tests (#12) share the host they talk to and the way to the page. The host answers
// what GET /organisation/pharmacies/mine, GET /organisation/pharmacies/{id}/members and …/invitations, and the five
// actions on them would, from the state a test gives it: an action the host accepts changes that state, so the lists
// the page reads again show what happened.

export type User = ReturnType<typeof userEvent.setup>;
export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/** The instant the tests run at: 2026-11-22, noon UTC. */
export const now = new Date('2026-11-22T12:00:00Z');

/** Fixes Date at `now` (or the instant given) and nothing else: timers, promises and userEvent keep running. */
export function fixClock(at: Date = now) {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(at);
}

/** A manager of the Apotheke am Markt, who may manage its members. */
export const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A membership of the same pharmacy that may assign courses but not manage members: the shell lets it in. */
export const coursesOnly: MyPharmacyDto = { ...markt, canManageMembers: false };

/** The caller's own active membership, the one with membershipId === markt.membershipId. */
export const you: PharmacyMemberDto = {
  membershipId: markt.membershipId,
  personId: '0198c1a0-0000-7000-8000-0000000000e0',
  name: 'Local Developer',
  jobFunction: 'Pharmacist',
  role: 'Manager',
  since: '2026-11-22T10:00:00Z',
};

export const anna: PharmacyMemberDto = {
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
  personId: '0198c1a0-0000-7000-8000-0000000000e1',
  name: 'Anna Albers',
  jobFunction: 'Pta',
  role: 'Employee',
  since: '2026-11-01T10:00:00Z',
};

/** An invitation still open at `now`: it expires on 2026-12-01. */
export const ben: PharmacyInvitationDto = {
  membershipId: '0198c1a0-0000-7000-8000-0000000000b3',
  email: 'ben@example.com',
  jobFunction: 'Pka',
  role: 'Employee',
  expiresAt: '2026-12-01T12:00:00Z',
};

/** An invitation expired at `now`: it expired on 2026-11-21. */
export const carla: PharmacyInvitationDto = {
  membershipId: '0198c1a0-0000-7000-8000-0000000000b4',
  email: 'carla@example.com',
  jobFunction: 'Trainee',
  role: 'Employee',
  expiresAt: '2026-11-21T12:00:00Z',
};

/** What the fake host answers; a test changes it before or while the page asks. */
export interface TeamHost {
  memberships: MyPharmacyDto[];
  members: PharmacyMemberDto[];
  invitations: PharmacyInvitationDto[];
  /** The members list's answer when it is not a page of the members: a failure, say. */
  membersAnswer?: () => Promise<Response>;
  /** The invitations list's answer when it is not a page of the invitations. */
  invitationsAnswer?: () => Promise<Response>;
  invite: (body: unknown) => Promise<Response>;
  renew: (membershipId: string) => Promise<Response>;
  revoke: (membershipId: string) => Promise<Response>;
  change: (membershipId: string, body: unknown) => Promise<Response>;
  end: (membershipId: string) => Promise<Response>;
}

const fourteenDays = 14 * 24 * 60 * 60 * 1000;
const ok = () => new Response(null, { status: 200 });

/** The host as it is before a test changes it: one manager (you), one employee, one open and one expired invitation. */
export function teamHost(): TeamHost {
  const host: TeamHost = {
    memberships: [markt],
    members: [you, anna],
    invitations: [carla, ben],
    invite: async (body) => {
      const { email, jobFunction, role } = body as Pick<PharmacyInvitationDto, 'email' | 'jobFunction' | 'role'>;
      const membershipId = '0198c1a0-0000-7000-8000-0000000000b9';
      host.invitations = [
        ...host.invitations,
        { membershipId, email, jobFunction, role, expiresAt: new Date(Date.now() + fourteenDays).toISOString() },
      ];
      return Response.json(membershipId, { status: 201 });
    },
    renew: async (membershipId) => {
      host.invitations = host.invitations.map((invitation) =>
        invitation.membershipId === membershipId
          ? { ...invitation, expiresAt: new Date(Date.now() + fourteenDays).toISOString() }
          : invitation,
      );
      return ok();
    },
    revoke: async (membershipId) => {
      host.invitations = host.invitations.filter((invitation) => invitation.membershipId !== membershipId);
      return ok();
    },
    change: async (membershipId, body) => {
      const { jobFunction, role } = body as Pick<PharmacyMemberDto, 'jobFunction' | 'role'>;
      host.members = host.members.map((member) =>
        member.membershipId === membershipId ? { ...member, jobFunction, role } : member,
      );
      if (membershipId === markt.membershipId) {
        host.memberships = host.memberships.map((membership) => ({ ...membership, jobFunction, role }));
      }
      return ok();
    },
    end: async (membershipId) => {
      host.members = host.members.filter((member) => member.membershipId !== membershipId);
      if (membershipId === markt.membershipId) {
        host.memberships = [];
      }
      return ok();
    },
  };
  return host;
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

const byName = (members: PharmacyMemberDto[]) =>
  [...members].sort((first, second) => first.name.localeCompare(second.name));
const byExpiry = (invitations: PharmacyInvitationDto[]) =>
  [...invitations].sort((first, second) => first.expiresAt.localeCompare(second.expiresAt));

const pharmacyPath =
  /^\/api\/organisation\/pharmacies\/([0-9a-f-]+)\/(members|invitations)(?:\/([0-9a-f-]+))?(\/renew)?$/;

/** The host as the generated client reaches it under /api, answering from the state. */
export function answering(host: TeamHost): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    const body =
      init?.body === undefined || init.body === null ? undefined : (JSON.parse(String(init.body)) as unknown);
    if (method === 'GET' && url.pathname === '/api/organisation/pharmacies/mine') {
      return page(host.memberships, url);
    }
    const match = pharmacyPath.exec(url.pathname);
    if (match === null || match[1] !== markt.id) {
      return new Response('', { status: 404 });
    }
    const [, , list, membershipId, renew] = match;
    if (list === 'members' && membershipId === undefined && method === 'GET') {
      return host.membersAnswer === undefined ? page(byName(host.members), url) : host.membersAnswer();
    }
    if (list === 'invitations' && membershipId === undefined && method === 'GET') {
      return host.invitationsAnswer === undefined ? page(byExpiry(host.invitations), url) : host.invitationsAnswer();
    }
    if (list === 'invitations' && membershipId === undefined && method === 'POST') {
      return host.invite(body);
    }
    if (list === 'invitations' && membershipId !== undefined && renew !== undefined && method === 'POST') {
      return host.renew(membershipId);
    }
    if (list === 'invitations' && membershipId !== undefined && renew === undefined && method === 'DELETE') {
      return host.revoke(membershipId);
    }
    if (list === 'members' && membershipId !== undefined && method === 'PATCH') {
      return host.change(membershipId, body);
    }
    if (list === 'members' && membershipId !== undefined && method === 'DELETE') {
      return host.end(membershipId);
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

/** An answer that never comes: the request stays pending. */
export const never = () => new Promise<Response>(() => undefined);

export interface Request {
  method: string;
  path: string;
  query: Record<string, string>;
  body: unknown;
}

/** Every request the host received, in order. */
export function requestsOf(fetch: { mock: { calls: Parameters<Fetch>[] } }): Request[] {
  return fetch.mock.calls.map(([input, init]) => {
    const url = new URL(String(input), 'http://localhost');
    return {
      method: init?.method ?? 'GET',
      path: url.pathname,
      query: Object.fromEntries(url.searchParams),
      body: init?.body === undefined || init.body === null ? undefined : (JSON.parse(String(init.body)) as unknown),
    };
  });
}

export const minePath = '/api/organisation/pharmacies/mine';
export const membersPath = `/api/organisation/pharmacies/${markt.id}/members`;
export const invitationsPath = `/api/organisation/pharmacies/${markt.id}/invitations`;

/** The GET requests the host received at a path, by their query. */
export function readsOf(fetch: { mock: { calls: Parameters<Fetch>[] } }, path: string) {
  return requestsOf(fetch)
    .filter((request) => request.method === 'GET' && request.path === path)
    .map((request) => request.query);
}

/** The requests that changed something (anything but a GET), in order. */
export function writesOf(fetch: { mock: { calls: Parameters<Fetch>[] } }) {
  return requestsOf(fetch)
    .filter((request) => request.method !== 'GET')
    .map(({ method, path, body }) => ({ method, path, body }));
}

/**
 * The page at /organisation with a Pinia and a query client of its own that does not retry, so a failure shows at once.
 * The page reads the current pharmacy from the memberships, as the shell does.
 */
export async function renderTeamPage(options: { pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/organisation');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(OrganisationPage, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, pinia };
}

/** The section of the active members, by its heading. */
export const membersSection = () => screen.getByRole('region', { name: t('organisation.team.members.title') });
/** The section of the invitations, by its heading. */
export const invitationsSection = () => screen.getByRole('region', { name: t('organisation.team.invitations.title') });

export const membersHeading = () =>
  screen.getByRole('heading', { level: 2, name: t('organisation.team.members.title') });
export const invitationsHeading = () =>
  screen.getByRole('heading', { level: 2, name: t('organisation.team.invitations.title') });

/** The table row that holds the text, once the page shows it. */
export async function rowOf(text: string) {
  const cell = await screen.findByText(text);
  const row = cell.closest('tr');
  if (row === null) {
    throw new Error(`${text} is not in a table row.`);
  }
  return within(row);
}

/** The header's Invite button. */
export const inviteButton = () => screen.getByRole('button', { name: t('organisation.team.invite.open') });

/** The member's change and end buttons, and the invitation's renew and revoke buttons, once the page lists them. */
export const changeButton = (member: PharmacyMemberDto) =>
  screen.findByRole('button', { name: t('organisation.team.members.changeOf', { name: member.name }) });
export const endButton = (member: PharmacyMemberDto) =>
  screen.findByRole('button', { name: t('organisation.team.members.endOf', { name: member.name }) });
export const renewButton = (invitation: PharmacyInvitationDto) =>
  screen.findByRole('button', { name: t('organisation.team.invitations.renewOf', { email: invitation.email }) });
export const revokeButton = (invitation: PharmacyInvitationDto) =>
  screen.findByRole('button', { name: t('organisation.team.invitations.revokeOf', { email: invitation.email }) });
