import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  type Fetch,
  answering,
  carl,
  hygiene,
  matrixHost,
  type MatrixHost,
  type MatrixAnswer,
  matrixOf,
  matrixPath,
  now,
  pagedTeam,
  readsOf,
  renderMatrixPage,
  teamMatrix,
} from './matrixFixtures';

// The team matrix's rate and filters (#36, T4). The rate says what share of the assignments is on track, over the
// filter. The job function is the host's filter and travels in the address; the course is the page's own: it narrows
// the columns to that course's and the rate to its tally, without asking the host again. The rate's figures are
// announced politely, and the focus stays where the manager is.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: MatrixHost;

/** The team as the spec's example: four of five on track, one overdue. */
const onTrack = (): MatrixAnswer => ({
  ...teamMatrix(),
  assignments: 5,
  compliantAssignments: 4,
  overdueAssignments: 1,
  complianceRate: 0.8,
});

/** The PTAs alone: Carl, one of his two courses on track, the other expired. */
const ptas = (): MatrixAnswer => ({
  ...teamMatrix(),
  members: { items: [carl], totalCount: 1, page: 1, pageSize: 50 },
  assignments: 2,
  compliantAssignments: 1,
  overdueAssignments: 0,
  complianceRate: 0.5,
});

/** Nobody with the job function asked for; the columns stay, their tallies empty. */
const nobody = (): MatrixAnswer => ({
  ...teamMatrix(),
  courses: teamMatrix().courses.map((course) => ({
    ...course,
    assignments: 0,
    compliantAssignments: 0,
    overdueAssignments: 0,
    complianceRate: null,
  })),
  members: { items: [], totalCount: 0, page: 1, pageSize: 50 },
  assignments: 0,
  compliantAssignments: 0,
  overdueAssignments: 0,
  complianceRate: null,
});

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  host = matrixHost();
  host.matrix = async (_pharmacy, url) =>
    Response.json(url.searchParams.get('jobFunction') === 'Pta' ? ptas() : onTrack());
  fetch.mockImplementation(answering(host));
});
afterEach(() => {
  vi.useRealTimers();
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const rate = () => screen.findByRole('region', { name: t('compliance.matrix.rate.title') });
const filters = () => screen.findByRole('group', { name: t('compliance.matrix.filters.label') });
const courseSelect = () => screen.findByRole('combobox', { name: t('compliance.matrix.filters.course') });
const jobFunctionSelect = () => screen.findByRole('combobox', { name: t('compliance.matrix.filters.jobFunction') });
const resetButton = () => screen.queryByRole('button', { name: t('compliance.matrix.filters.reset') });
const courseHeaders = () =>
  within(screen.getByRole('table'))
    .getAllByRole('columnheader')
    .slice(1)
    .map((header) => header.textContent ?? '');

describe('the rate', () => {
  test('says the share on track, the counts, the overdue ones and what it covers', async () => {
    await renderMatrixPage();

    const section = await rate();
    expect(within(section).getByRole('heading', { level: 2, name: 'Compliance-Quote' })).toBeInTheDocument();
    const figure = await within(section).findByText('80 %');
    expect(figure).toHaveClass('text-display', 'tabular-nums');
    expect(section).toHaveTextContent('Fristgerecht: 4 von 5');
    expect(section).toHaveTextContent('Eine ist überfällig.');
    expect(section).toHaveTextContent('Bezogen auf: Alle Schulungen; Alle Tätigkeiten');
    expect(section).toHaveTextContent(
      'Fristgerecht ist eine Zuweisung, solange sie nicht überfällig ist und ihr Abschluss noch gilt.',
    );
  });

  test('says it in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    await renderMatrixPage();

    const section = await rate();
    expect(await within(section).findByText('80%')).toBeInTheDocument();
    expect(section).toHaveTextContent('On track: 4 of 5');
    expect(section).toHaveTextContent('One is overdue.');
    expect(section).toHaveTextContent('Covers: All courses; All job functions');
  });

  test('announces its figures politely, so a filter’s new figures are heard', async () => {
    await renderMatrixPage();

    const summary = await within(await rate()).findByText('Fristgerecht: 4 von 5');
    expect(summary.closest('[aria-live="polite"]')).not.toBeNull();
  });

  test('without any assignment shows a dash and says there is no rate yet', async () => {
    host.matrix = async () =>
      Response.json({
        ...matrixOf([], []),
        assignments: 0,
        compliantAssignments: 0,
        overdueAssignments: 0,
        complianceRate: null,
      });
    await renderMatrixPage();

    const section = await rate();
    expect(await within(section).findByText('–')).toBeInTheDocument();
    expect(section).toHaveTextContent('Noch keine Zuweisungen, daher keine Quote.');
    expect(section).not.toHaveTextContent('%');
  });

  test('none overdue says so', async () => {
    host.matrix = async () => Response.json({ ...onTrack(), overdueAssignments: 0 });
    await renderMatrixPage();

    expect(await rate()).toHaveTextContent('Keine ist überfällig.');
  });
});

describe('the filters', () => {
  test('are a labelled group of a course and a job function, each with all and its choices', async () => {
    await renderMatrixPage();

    const group = await filters();
    expect(group.tagName).toBe('FIELDSET');
    const course = within(group).getByRole('combobox', { name: 'Schulung' });
    await waitFor(() =>
      expect(
        within(course)
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Alle Schulungen', 'Datenschutz in der Apotheke', 'Hygiene in der Apotheke', 'Notfallmanagement']),
    );
    const jobFunction = within(group).getByRole('combobox', { name: 'Tätigkeit' });
    expect(
      within(jobFunction)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual([
      'Alle Tätigkeiten',
      'Apotheker',
      'PTA (pharmazeutisch-technischer Assistent)',
      'PKA (pharmazeutisch-kaufmännischer Angestellter)',
      'Azubi',
      'Sonstige',
    ]);
    expect(resetButton()).not.toBeInTheDocument();
  });

  test('a job function goes to the host through the address, from the first page, and the rate follows', async () => {
    const user = userEvent.setup();
    const { router } = await renderMatrixPage();

    await within(await rate()).findByText('80 %');
    const select = await jobFunctionSelect();
    await user.selectOptions(select, 'PTA (pharmazeutisch-technischer Assistent)');

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ jobFunction: 'Pta' }));
    await waitFor(() =>
      expect(readsOf(fetch, matrixPath())).toContainEqual({ page: '1', pageSize: '50', jobFunction: 'Pta' }),
    );
    const section = await rate();
    expect(await within(section).findByText('50 %')).toBeInTheDocument();
    expect(section).toHaveTextContent('Fristgerecht: 1 von 2');
    expect(section).toHaveTextContent('Bezogen auf: Alle Schulungen; PTA (pharmazeutisch-technischer Assistent)');
    expect(select).toHaveFocus();
  });

  test('a job function chosen on a later page starts again from the first', async () => {
    const user = userEvent.setup();
    host.matrix = pagedTeam(60);
    const { router } = await renderMatrixPage({ address: '/compliance?page=2' });

    await screen.findByText('Einträge 51–60 von 60');
    await user.selectOptions(await jobFunctionSelect(), 'Azubi');

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ jobFunction: 'Trainee' }));
    await waitFor(() =>
      expect(readsOf(fetch, matrixPath())).toContainEqual({ page: '1', pageSize: '50', jobFunction: 'Trainee' }),
    );
  });

  test('the job function in the address is chosen when the page opens', async () => {
    await renderMatrixPage({ address: '/compliance?jobFunction=Pta' });

    expect(await jobFunctionSelect()).toHaveDisplayValue('PTA (pharmazeutisch-technischer Assistent)');
    expect(readsOf(fetch, matrixPath())).toEqual([{ page: '1', pageSize: '50', jobFunction: 'Pta' }]);
  });

  test('a course is the page’s own: it goes into the address and asks the host nothing', async () => {
    const user = userEvent.setup();
    const { router } = await renderMatrixPage();

    await within(await rate()).findByText('80 %');
    const select = await courseSelect();
    await user.selectOptions(select, 'Hygiene in der Apotheke');

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ course: hygiene.courseId }));
    expect(readsOf(fetch, matrixPath())).toEqual([{ page: '1', pageSize: '50' }]);
    expect(select).toHaveFocus();
  });

  test('a course leaves the member column and its own column only', async () => {
    await renderMatrixPage({ address: `/compliance?course=${hygiene.courseId}` });

    await screen.findByRole('rowheader', { name: /^Anna Albers/ });
    await waitFor(() => expect(courseHeaders()).toHaveLength(1));
    expect(courseHeaders()[0]).toContain('Hygiene in der Apotheke');
    expect(within(screen.getByRole('table')).getAllByRole('columnheader')[0]).toHaveTextContent('Mitglied');
    // The rows stay the host's: a member without the course shows it as not assigned.
    expect(within(screen.getByRole('table')).getAllByRole('rowheader')).toHaveLength(4);
  });

  test('a course shows its own rate, counts and scope instead of the totals', async () => {
    await renderMatrixPage({ address: `/compliance?course=${hygiene.courseId}` });

    const section = await rate();
    expect(await within(section).findByText('50 %')).toBeInTheDocument();
    expect(section).toHaveTextContent('Fristgerecht: 1 von 2');
    expect(section).toHaveTextContent('Eine ist überfällig.');
    expect(section).toHaveTextContent('Bezogen auf: Hygiene in der Apotheke; Alle Tätigkeiten');
    expect(section).not.toHaveTextContent('80 %');
  });

  test('a course chosen on a later page keeps the page', async () => {
    const user = userEvent.setup();
    host.matrix = pagedTeam(60);
    const { router } = await renderMatrixPage({ address: '/compliance?page=2' });

    await screen.findByText('Einträge 51–60 von 60');
    await user.selectOptions(await courseSelect(), 'Hygiene in der Apotheke');

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ course: hygiene.courseId, page: '2' }));
  });

  test('a course the matrix does not have shows every column and leaves the address', async () => {
    const { router } = await renderMatrixPage({
      address: '/compliance?course=0198c1a0-0000-7000-8000-00000000dead&jobFunction=Pka',
    });

    await screen.findByRole('rowheader', { name: /^Anna Albers/ });
    expect(courseHeaders()).toHaveLength(3);
    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ jobFunction: 'Pka' }));
    expect(await courseSelect()).toHaveDisplayValue('Alle Schulungen');
  });

  test('Reset shows the whole team again and puts the focus on the job function', async () => {
    const user = userEvent.setup();
    const { router } = await renderMatrixPage({
      address: `/compliance?course=${hygiene.courseId}&jobFunction=Pta`,
    });

    await within(await rate()).findByText('50 %');
    await user.click(await within(await filters()).findByRole('button', { name: 'Filter zurücksetzen' }));

    await waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/compliance'));
    expect(await jobFunctionSelect()).toHaveFocus();
    expect(await courseSelect()).toHaveDisplayValue('Alle Schulungen');
    expect(await jobFunctionSelect()).toHaveDisplayValue('Alle Tätigkeiten');
    await waitFor(() => expect(resetButton()).not.toBeInTheDocument());
  });

  test('a job function nobody has says so with its name, and its reset clears it', async () => {
    const user = userEvent.setup();
    host.matrix = async (_pharmacy, url) =>
      Response.json(url.searchParams.get('jobFunction') === 'Pta' ? nobody() : onTrack());
    const { router } = await renderMatrixPage({ address: '/compliance?jobFunction=Pta' });

    const said = await screen.findByText(
      'Niemand in dieser Apotheke hat die Tätigkeit PTA (pharmazeutisch-technischer Assistent).',
    );
    const empty = said.closest<HTMLElement>('[data-slot="empty-state"]');
    expect(empty).not.toBeNull();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await user.click(within(empty ?? said).getByRole('button', { name: 'Filter zurücksetzen' }));

    await waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/compliance'));
    expect(await jobFunctionSelect()).toHaveFocus();
    expect(await screen.findByRole('rowheader', { name: /^Anna Albers/ })).toBeInTheDocument();
  });

  test('are labelled in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    await renderMatrixPage({ address: '/compliance?jobFunction=Pta' });

    const group = await screen.findByRole('group', { name: 'Filters' });
    const course = within(group).getByRole('combobox', { name: 'Course' });
    expect(within(course).getAllByRole('option')[0]).toHaveTextContent('All courses');
    const jobFunction = within(group).getByRole('combobox', { name: 'Job function' });
    expect(within(jobFunction).getAllByRole('option')[0]).toHaveTextContent('All job functions');
    expect(within(group).getByRole('button', { name: 'Reset filters' })).toBeInTheDocument();
  });
});
