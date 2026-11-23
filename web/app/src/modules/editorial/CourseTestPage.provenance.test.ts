import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, within } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialQuestionDto } from '@/api/generated';
import { i18n, type Language, setLanguage, t } from '@/i18n';
import { de } from '@/i18n/de';
import { en } from '@/i18n/en';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { approvedAi, handWritten, versionId, versionOf } from './courseTestFixtures';

// Where a question came from and who approved it, when (#25, story E7): the card's last caption line reads "KI-entworfen,
// geprüft von X am Y", the day in the user's time zone. An approval without a name (made before #205) gives the day
// alone; a question not yet approved keeps its unreviewed line. The line is plain text, read with the card, never
// announced.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;
const timeZone = process.env.TZ;

beforeEach(() => {
  // The user's time zone: d() formats in the browser's, and the day of an instant depends on it.
  process.env.TZ = 'Europe/Berlin';
  setLanguage('de-DE', { remember: false });
  version = versionOf('Draft');
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if ((init?.method ?? 'GET') === 'GET' && url.pathname === `/api/editorial/course-versions/${versionId}`) {
      return Response.json(version);
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());
afterEach(cleanup);
afterEach(() => {
  // Node applies a change of TZ at once; the test process gets its own zone back.
  if (timeZone === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = timeZone;
  }
});

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

/** The page showing the one question, its card the list's only item. */
async function renderWith(question: EditorialQuestionDto) {
  version = versionOf('Draft', { questions: [question] });
  const rendered = await renderPage();
  return { ...rendered, card: () => onlyCard() };
}

function onlyCard(): HTMLElement {
  const list = screen.getByRole('list', { name: t('editorial.test.questions.title') });
  const cards = [...list.querySelectorAll(':scope > li')];
  expect(cards).toHaveLength(1);
  const [card] = cards;
  return card instanceof HTMLElement ? card : document.body;
}

/** Switches to English and waits for the page to have re-rendered in it. */
async function inEnglish() {
  setLanguage('en-GB', { remember: false });
  await screen.findByRole('heading', { level: 2, name: 'Questions' });
}

// The new keys are not in the catalogs' schema yet: they are translated by name, as catalogs.test.ts does.
const translate = i18n.global.t as unknown as (
  key: string,
  named: Record<string, string>,
  options: { locale: Language },
) => string;

const approvedOn = '2026-11-20T09:30:00Z';
const approvedBy = (question: EditorialQuestionDto, changes: Partial<EditorialQuestionDto> = {}) => ({
  ...question,
  review: 'Approved' as const,
  reviewedBy: '0198c1a0-0000-7000-8000-0000000000d9',
  reviewedAt: approvedOn,
  approvedByName: 'Emma Editor',
  approvedAt: approvedOn,
  ...changes,
});

const provenanceOf = (catalog: typeof de | typeof en) =>
  catalog.editorial.test.questions.provenance as Record<string, string | undefined>;

describe('the approval line, with the approver’s name', () => {
  test.each([
    ['de-DE', '{origin}, geprüft von {name} am {date}'],
    ['en-GB', '{origin}, approved by {name} on {date}'],
  ] as const)('editorial.test.questions.provenance.approved reads in %s: %s', (language, text) => {
    expect(provenanceOf(language === 'de-DE' ? de : en).approved).toBe(text);
  });

  test('an AI-drafted question approved by Emma Editor reads KI-entworfen, geprüft von Emma Editor am 20.11.2026', async () => {
    const { card } = await renderWith(approvedBy(approvedAi));

    const line = within(card()).getByText('KI-entworfen, geprüft von Emma Editor am 20.11.2026');
    expect(line).toHaveTextContent(
      translate(
        'editorial.test.questions.provenance.approved',
        { origin: t('glossary.questionProvenance.AiDrafted'), name: 'Emma Editor', date: '20.11.2026' },
        { locale: 'de-DE' },
      ),
    );
    // One caption line, a plain sentence: no element of its own for the date.
    expect(line.tagName).toBe('P');
    expect(line.querySelector('time')).toBeNull();
    expect(line).toHaveClass('text-caption', 'text-muted-foreground');
  });

  test('a hand-written question approved by Emma Editor reads Von Hand geschrieben, geprüft von Emma Editor am 20.11.2026', async () => {
    const { card } = await renderWith(approvedBy(handWritten));

    expect(within(card()).getByText('Von Hand geschrieben, geprüft von Emma Editor am 20.11.2026')).toBeInTheDocument();
  });

  test('the date is the user’s day: approved at 23:30 UTC, it reads 21.11.2026 in Europe/Berlin', async () => {
    const { card } = await renderWith(approvedBy(approvedAi, { approvedAt: '2026-11-20T23:30:00Z' }));

    expect(within(card()).getByText('KI-entworfen, geprüft von Emma Editor am 21.11.2026')).toBeInTheDocument();
  });

  test('switched to English, reads AI-drafted, approved by Emma Editor on 20/11/2026', async () => {
    const { card } = await renderWith(approvedBy(approvedAi));

    await inEnglish();

    expect(await within(card()).findByText('AI-drafted, approved by Emma Editor on 20/11/2026')).toBeInTheDocument();
  });

  test('switched to English, a hand-written one reads Written by hand, approved by Emma Editor on 20/11/2026', async () => {
    const { card } = await renderWith(approvedBy(handWritten));

    await inEnglish();

    expect(
      await within(card()).findByText('Written by hand, approved by Emma Editor on 20/11/2026'),
    ).toBeInTheDocument();
  });

  test('the line is read with the card, not announced: it is in no live region', async () => {
    const { card } = await renderWith(approvedBy(approvedAi));

    const line = within(card()).getByText('KI-entworfen, geprüft von Emma Editor am 20.11.2026');
    expect(line.closest('[role=status],[aria-live]')).toBeNull();
    expect(line).not.toHaveAttribute('role');
  });

  test('a name holding markup is shown as text', async () => {
    const { card, container } = await renderWith(approvedBy(approvedAi, { approvedByName: '<b>X</b>' }));

    expect(within(card()).getByText('KI-entworfen, geprüft von <b>X</b> am 20.11.2026')).toBeInTheDocument();
    expect(container.querySelector('b')).toBeNull();
  });
});

describe('the approval line, without the approver’s name', () => {
  test.each([
    ['de-DE', '{origin}, geprüft am {date}'],
    ['en-GB', '{origin}, approved on {date}'],
  ] as const)('editorial.test.questions.provenance.approvedUnnamed reads in %s: %s', (language, text) => {
    expect(provenanceOf(language === 'de-DE' ? de : en).approvedUnnamed).toBe(text);
  });

  test.each([
    ['no name', null],
    ['an empty name', ''],
    ['a blank name', '  '],
  ])('an approval with %s reads KI-entworfen, geprüft am 20.11.2026', async (_, approvedByName) => {
    const { card } = await renderWith(approvedBy(approvedAi, { approvedByName }));

    const line = within(card()).getByText('KI-entworfen, geprüft am 20.11.2026');
    expect(line).toHaveTextContent(
      translate(
        'editorial.test.questions.provenance.approvedUnnamed',
        { origin: t('glossary.questionProvenance.AiDrafted'), date: '20.11.2026' },
        { locale: 'de-DE' },
      ),
    );
    expect(card()).not.toHaveTextContent(/geprüft von/);
  });

  test.each([
    ['no name', null],
    ['an empty name', ''],
    ['a blank name', '  '],
  ])('switched to English, an approval with %s reads AI-drafted, approved on 20/11/2026', async (_, approvedByName) => {
    const { card } = await renderWith(approvedBy(approvedAi, { approvedByName }));

    await inEnglish();

    expect(await within(card()).findByText('AI-drafted, approved on 20/11/2026')).toBeInTheDocument();
    expect(card()).not.toHaveTextContent(/approved by/);
  });

  test('a hand-written question approved without a name reads Von Hand geschrieben, geprüft am 20.11.2026', async () => {
    const { card } = await renderWith(approvedBy(handWritten, { approvedByName: null }));

    expect(within(card()).getByText('Von Hand geschrieben, geprüft am 20.11.2026')).toBeInTheDocument();
  });
});

// Guards, not new behaviour: what a question not yet approved says stays as it was.
describe('a question not approved', () => {
  test('an approval reset to unreviewed reads KI-entworfen, noch nicht geprüft, with no name and no date', async () => {
    const { card } = await renderWith(
      approvedBy(approvedAi, {
        review: 'Unreviewed',
        reviewedBy: null,
        reviewedAt: null,
        approvedByName: null,
        approvedAt: null,
      }),
    );

    expect(within(card()).getByText('KI-entworfen, noch nicht geprüft')).toBeInTheDocument();
    expect(within(card()).getByText('Ungeprüft')).toBeInTheDocument();
    expect(card()).not.toHaveTextContent(/\d{2}\.\d{2}\.\d{4}/);
    expect(card()).not.toHaveTextContent(/geprüft (von|am)/);
  });

  test('an approved question without a time of approval shows its origin alone', async () => {
    const { card } = await renderWith(approvedBy(approvedAi, { approvedAt: null }));

    expect(within(card()).getByText('KI-entworfen')).toBeInTheDocument();
    expect(card()).not.toHaveTextContent(/geprüft (von|am)/);
  });
});
