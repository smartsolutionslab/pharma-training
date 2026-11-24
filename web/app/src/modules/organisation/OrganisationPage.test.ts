import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { i18n, setLanguage, t } from '@/i18n';
import {
  anna,
  answering,
  coursesOnly,
  fixClock,
  type Fetch,
  invitationsPath,
  invitationsSection,
  inviteButton,
  markt,
  membersHeading,
  membersPath,
  membersSection,
  minePath,
  never,
  problem,
  readsOf,
  renderTeamPage,
  rowOf,
  type TeamHost,
  teamHost,
  you,
} from './teamFixtures';
import { titleKey } from './titleKey';

// The members and invitations page (#12) replaces the module's sample at /organisation. A manager sees the pharmacy's
// active members, by name: their name, job function and role in the glossary's words, and since when, as a date in the
// language shown. The caller's own row says so. The page asks the host only for a membership that may manage members
// (ADR 0003: the capability decides, the page only hides); it reads both lists as one page of at most 100.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: TeamHost;

beforeEach(() => {
  localStorage.clear();
  fixClock();
  host = teamHost();
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  fetch.mockReset();
  vi.useRealTimers();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

test('the page is headed by the module’s title, says whose team it is and offers to invite', async () => {
  await renderTeamPage();

  const main = within(screen.getByRole('main'));
  const heading = main.getByRole('heading', { level: 1, name: t(titleKey) });
  expect(heading).toHaveClass('text-title');
  expect(await main.findByText(t('organisation.team.intro', { pharmacy: 'Apotheke am Markt' }))).toBeInTheDocument();
  expect(inviteButton()).toBeEnabled();
});

test('the page has a section for the active members and one for the invitations, each named by its heading', async () => {
  await renderTeamPage();

  await waitFor(() => expect(membersSection()).toBeInTheDocument());
  expect(invitationsSection()).toBeInTheDocument();
  expect(membersHeading()).toHaveAttribute('tabindex', '-1');
  expect(screen.getByRole('heading', { level: 2, name: t('organisation.team.invitations.title') })).toHaveAttribute(
    'tabindex',
    '-1',
  );
});

test('the members table names its columns', async () => {
  await renderTeamPage();

  const members = within(await waitFor(() => membersSection()));
  expect(
    members
      .getAllByRole('columnheader')
      .map((header) => header.textContent.trim())
      .filter((name) => name !== ''),
  ).toEqual(
    expect.arrayContaining([
      t('organisation.team.columns.name'),
      t('organisation.team.columns.jobFunction'),
      t('organisation.team.columns.role'),
      t('organisation.team.columns.since'),
    ]),
  );
});

test('the page lists the active members by name, with their job function, role and since when in German', async () => {
  await renderTeamPage();

  const row = await rowOf('Anna Albers');
  expect(row.getByText(t('glossary.jobFunction.Pta'))).toBeInTheDocument();
  expect(row.getByText(t('glossary.membershipRole.Employee'))).toBeInTheDocument();
  expect(row.getByText('01.11.2026')).toBeInTheDocument();
  expect(row.getByText('PTA (pharmazeutisch-technischer Assistent)')).toBeInTheDocument();
  expect(row.getByText('Mitarbeiter')).toBeInTheDocument();
});

test('the caller’s own row says it is theirs', async () => {
  await renderTeamPage();

  const row = await rowOf('Local Developer (Sie)');
  expect(row.getByText(t('glossary.jobFunction.Pharmacist'))).toBeInTheDocument();
  expect(row.getByText('Apothekenleiter')).toBeInTheDocument();
  expect(row.getByText('22.11.2026')).toBeInTheDocument();
  expect(screen.queryByText('Local Developer')).not.toBeInTheDocument();
});

test('the members are dated and named in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  await renderTeamPage();

  const own = await rowOf('Local Developer (you)');
  expect(own.getByText('22/11/2026')).toBeInTheDocument();
  expect(own.getByText(t('glossary.membershipRole.Manager'))).toBeInTheDocument();
  const other = await rowOf('Anna Albers');
  expect(other.getByText('01/11/2026')).toBeInTheDocument();
  expect(other.getByText(t('glossary.jobFunction.Pta'))).toBeInTheDocument();
  expect(t('glossary.jobFunction.Pta')).not.toBe(i18n.global.t('glossary.jobFunction.Pta', {}, { locale: 'de-DE' }));
});

test('every member’s row, the caller’s own included, offers to change and to end the membership', async () => {
  await renderTeamPage();

  for (const name of [anna.name, you.name]) {
    expect(
      await screen.findByRole('button', { name: t('organisation.team.members.changeOf', { name }) }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: t('organisation.team.members.endOf', { name }) })).toBeInTheDocument();
  }
});

test('below the members the table says which of how many it shows', async () => {
  await renderTeamPage();

  await rowOf('Anna Albers');
  const { n } = i18n.global;
  expect(within(membersSection()).getByRole('table')).toHaveAccessibleDescription(
    t('dataTable.showing', { first: n(1, 'integer'), last: n(2, 'integer'), total: n(2, 'integer') }),
  );
});

test('the page reads both lists as the first page of 100', async () => {
  await renderTeamPage();

  await rowOf('Anna Albers');
  expect(readsOf(fetch, membersPath)).toEqual([{ page: '1', pageSize: '100' }]);
  await waitFor(() => expect(readsOf(fetch, invitationsPath)).toEqual([{ page: '1', pageSize: '100' }]));
});

test('while the members load the table is busy and shows skeleton rows', async () => {
  host.membersAnswer = never;
  await renderTeamPage();

  await waitFor(() => expect(membersSection().querySelector('[aria-busy="true"]')).not.toBeNull());
  expect(membersSection().querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(within(membersSection()).getByText(t('dataTable.loading'))).toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.members.empty'))).not.toBeInTheDocument();
});

test('members that cannot be loaded are reported in their section', async () => {
  host.membersAnswer = async () => new Response('', { status: 500 });
  await renderTeamPage();

  const alert = await within(await waitFor(() => membersSection())).findByRole('alert');
  expect(alert).toHaveTextContent(t('organisation.team.members.loadFailed'));
  expect(alert.tagName).toBe('P');
  expect(within(membersSection()).queryByRole('table')).not.toBeInTheDocument();
});

// A refusal the page has words for says what it is, not merely that the list failed: the host's words never show.
test.each([
  [403, 'organisation.capability-missing', 'errors.organisation.capability-missing'],
  [404, 'organisation.pharmacy-not-found', 'errors.organisation.pharmacy-not-found'],
])('members the host refuses with %i %s say why', async (status, code, key) => {
  host.membersAnswer = async () => problem(status, code, 'Words of the host, not for the user.');
  await renderTeamPage();

  const alert = await within(await waitFor(() => membersSection())).findByRole('alert');
  expect(alert).toHaveTextContent(t(key));
  expect(alert).not.toHaveTextContent('Words of the host');
});

// A list refused because the membership is gone, or may no longer manage members, reads the caller's memberships
// again, as a refused action does: the page then follows what the host says the caller now is.
test.each([
  ['members', 403, 'organisation.capability-missing'],
  ['members', 404, 'organisation.pharmacy-not-found'],
  ['invitations', 403, 'organisation.capability-missing'],
  ['invitations', 404, 'organisation.pharmacy-not-found'],
] as const)('%s the host refuses with %i %s read the caller’s memberships again', async (list, status, code) => {
  const refuse = async () => {
    host.memberships = status === 403 ? [coursesOnly] : [];
    return problem(status, code, 'x');
  };
  if (list === 'members') {
    host.membersAnswer = refuse;
  } else {
    host.invitationsAnswer = refuse;
  }
  await renderTeamPage();

  await waitFor(() => expect(readsOf(fetch, minePath).length).toBeGreaterThan(1));
  if (status === 403) {
    expect(await screen.findByText(t('organisation.team.notAllowed', { pharmacy: markt.name }))).toBeInTheDocument();
  }
  await waitFor(() => expect(screen.queryByRole('region')).not.toBeInTheDocument());
});

test('invitations that cannot be loaded are reported in their section, and the members still show', async () => {
  host.invitationsAnswer = async () => new Response('', { status: 500 });
  await renderTeamPage();

  const alert = await within(await waitFor(() => invitationsSection())).findByRole('alert');
  expect(alert).toHaveTextContent(t('organisation.team.invitations.loadFailed'));
  expect(await rowOf('Anna Albers')).toBeTruthy();
});

// ADR 0003: the shell lets in a membership that may assign courses only; the page itself needs the capability to
// manage members, says so without it and asks the host for neither list.
test('a membership that may not manage members is told so, and neither list is read', async () => {
  host.memberships = [coursesOnly];
  await renderTeamPage();

  expect(await screen.findByText(t('organisation.team.notAllowed', { pharmacy: markt.name }))).toBeInTheDocument();
  expect(screen.getByRole('heading', { level: 1, name: t(titleKey) })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: t('organisation.team.invite.open') })).not.toBeInTheDocument();
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
  expect(readsOf(fetch, membersPath)).toEqual([]);
  expect(readsOf(fetch, invitationsPath)).toEqual([]);
});

// The German texts themselves, not only their keys: a key that resolved to the wrong text would pass the t() queries.
test('the page speaks German by default', async () => {
  await renderTeamPage();

  expect(screen.getByRole('heading', { level: 1, name: 'Mitglieder' })).toBeInTheDocument();
  expect(await screen.findByRole('heading', { level: 2, name: 'Aktive Mitglieder' })).toBeInTheDocument();
  expect(screen.getByRole('heading', { level: 2, name: 'Einladungen' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Einladen' })).toBe(inviteButton());
  expect(await screen.findByRole('button', { name: 'Anna Albers austragen' })).toBeInTheDocument();
});

test('switched to English while shown, the page speaks English', async () => {
  await renderTeamPage();
  await rowOf('Local Developer (Sie)');

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('heading', { level: 2, name: 'Active members' })).toBeInTheDocument();
  expect(await screen.findByText('Local Developer (you)')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Invite' })).toBeInTheDocument();
});
