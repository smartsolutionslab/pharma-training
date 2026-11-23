import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import type { EditorialCourseVersionStatus } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import { versionId, versionOf } from './courseTestFixtures';
import {
  answering,
  editorSub,
  type Fetch,
  refused,
  renderVersionPage,
  returnedAtNow,
  returnRecord,
  type WorkflowHost,
  workflowHost,
} from './workflowPageHost';

// The course version page's return history (#201, ADR 0013): a section "Rückgaben" below the workflow section and
// above the chapters, in every status, since the history is an audit trail and stays readable once the version is
// published or retired. A return taken on the page shows as the history's first row without a reload: the step's
// invalidation reads the history again, and the history goes back to its first page, where the new record is. A
// refused return stores nothing (ADR 0013), so the history is not read again.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: WorkflowHost;

beforeEach(() => {
  host = workflowHost(versionOf('InReview'));
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const title = 'Hygiene in der Apotheke';
const renderPage = () => renderVersionPage(versionId, title);

const older = returnRecord(2, 'Die Lernziele fehlen.');
const oldest = returnRecord(1, 'Frage 3 ist doppelt.', editorSub);

/** The history's region, by its heading. */
const history = (name = 'Rückgaben') => screen.getByRole('region', { name });
/** The history's data rows, the header row left out. */
const rows = () => within(within(history()).getByRole('table')).getAllByRole('row').slice(1);
const cells = (row: HTMLElement | undefined) => within(row ?? document.body).getAllByRole('cell');
/** The text of each data row's cells: the date, who returned it and the reason. */
const shownRows = () => rows().map((row) => cells(row).map((cell) => cell.textContent.trim()));

const dateTime = (instant: string) => i18n.global.d(new Date(instant), 'dateTime');

/** The pages of the history the page asked the host for, in order. */
const historyReads = () =>
  fetch.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') === 'GET')
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === `/api/editorial/course-versions/${versionId}/returns`)
    .map((url) => url.searchParams.get('page'));

/** Returns the version in review with the reason, through the page's dialog. */
async function returnWith(reason: string) {
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: t('editorial.workflow.return') }));
  const dialog = await screen.findByRole('dialog', { name: t('editorial.workflow.returnForm.title') });
  await user.type(within(dialog).getByRole('textbox', { name: t('editorial.workflow.returnForm.reason') }), reason);
  await user.click(within(dialog).getByRole('button', { name: t('editorial.workflow.returnForm.confirm') }));
}

describe('the return history', () => {
  const statuses: EditorialCourseVersionStatus[] = ['Draft', 'InReview', 'Published', 'Superseded', 'Retired'];

  test.each(statuses)(
    'of a version %s is the section "Rückgaben", below the workflow section and above the chapters',
    async (status) => {
      host.version = versionOf(status);
      host.returns = [older, oldest];
      const { heading } = await renderPage();

      const region = await screen.findByRole('region', { name: 'Rückgaben' });
      expect(within(region).getByRole('heading', { level: 2, name: 'Rückgaben' })).toBeVisible();
      expect(await within(region).findByText(older.reason)).toBeVisible();
      const chapters = screen.getByRole('heading', { level: 2, name: t('editorial.chapters.title') });
      const workflow = screen.queryByRole('region', { name: t('editorial.workflow.title') });
      // A retired version offers no step, so it has no workflow section: the history follows the header.
      const before = workflow ?? heading;
      expect(before.compareDocumentPosition(region) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(region.compareDocumentPosition(chapters) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    },
  );

  test("lists the version's returns in the host's order: when, by whom and why", async () => {
    host.returns = [older, oldest];
    await renderPage();

    await within(await screen.findByRole('region', { name: 'Rückgaben' })).findByText(older.reason);
    expect(shownRows()).toEqual([
      [dateTime(older.returnedAt), 'einer anderen Person aus der Redaktion', older.reason],
      [dateTime(oldest.returnedAt), 'Ihnen', oldest.reason],
    ]);
    expect(historyReads()).toEqual(['1']);
  });

  test('is headed "Returns" in English', async () => {
    setLanguage('en-GB', { remember: false });
    host.returns = [older];
    await renderPage();

    const region = await screen.findByRole('region', { name: 'Returns' });
    expect(within(region).getByRole('heading', { level: 2, name: 'Returns' })).toBeVisible();
    expect(await within(region).findByText(older.reason)).toBeVisible();
  });

  test('of a version never returned says so', async () => {
    await renderPage();

    const region = await screen.findByRole('region', { name: 'Rückgaben' });
    expect(
      await within(region).findByText('Diese Version wurde noch nie zur Überarbeitung zurückgegeben.'),
    ).toBeVisible();
  });

  test('shows a reason that looks like markup as the characters it is', async () => {
    host.returns = [returnRecord(1, '<img src=x onerror=alert(1)> <b>fett</b>')];
    await renderPage();

    const region = await screen.findByRole('region', { name: 'Rückgaben' });
    expect(await within(region).findByText('<img src=x onerror=alert(1)> <b>fett</b>')).toBeVisible();
    expect(region.querySelector('img, b')).toBeNull();
  });
});

describe('a return taken on the page', () => {
  const reason = 'Kapitel 2 braucht eine Quelle.';

  test('shows as the first row without a reload, the history read again', async () => {
    host.returns = [older];
    const { heading } = await renderPage();
    await within(await screen.findByRole('region', { name: 'Rückgaben' })).findByText(older.reason);
    expect(historyReads()).toEqual(['1']);

    await returnWith(reason);

    await waitFor(() => expect(heading).toHaveFocus());
    await waitFor(() => expect(shownRows()[0]).toEqual([dateTime(returnedAtNow), 'Ihnen', reason]));
    expect(shownRows()[1]).toEqual([
      dateTime(older.returnedAt),
      'einer anderen Person aus der Redaktion',
      older.reason,
    ]);
    expect(historyReads()).toEqual(['1', '1']);
  });

  test('takes the history back to its first page, where the new return is', async () => {
    // 21 returns: the history has a second page, holding the oldest.
    host.returns = Array.from({ length: 21 }, (_, index) => returnRecord(21 - index, `Grund ${21 - index}`));
    const user = userEvent.setup();
    const { heading } = await renderPage();
    await within(await screen.findByRole('region', { name: 'Rückgaben' })).findByText('Grund 21');
    await user.click(within(history()).getByRole('button', { name: t('dataTable.next') }));
    await waitFor(() => expect(shownRows()[0]?.[2]).toBe('Grund 1'));

    await returnWith(reason);

    await waitFor(() => expect(heading).toHaveFocus());
    await waitFor(() => expect(shownRows()[0]).toEqual([dateTime(returnedAtNow), 'Ihnen', reason]));
    expect(shownRows()[1]?.[2]).toBe('Grund 21');
    expect(historyReads().at(-1)).toBe('1');
  });

  test('refused, adds no row and does not read the history again', async () => {
    host.returns = [older];
    host.step = async () => {
      host.version = versionOf('Published');
      return refused(409, 'editorial.not-in-review');
    };
    await renderPage();
    await within(await screen.findByRole('region', { name: 'Rückgaben' })).findByText(older.reason);
    const readsBefore = historyReads().length;

    await returnWith(reason);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Version ist nicht mehr in Prüfung.'));
    // The version is read again, and shows it is published: the refusal's own invalidation has run by now.
    await waitFor(() => expect(screen.getByText(t('glossary.courseVersionStatus.Published'))).toBeVisible());
    expect(historyReads()).toHaveLength(readsBefore);
    expect(shownRows()).toEqual([[dateTime(older.returnedAt), 'einer anderen Person aus der Redaktion', older.reason]]);
    expect(host.returns).toEqual([older]);
  });
});
