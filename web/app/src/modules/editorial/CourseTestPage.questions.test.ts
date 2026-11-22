import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, within } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialQuestionDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { approvedAi, handWritten, introduction, unreviewedAi, versionId, versionOf } from './courseTestFixtures';

// The test's questions (#25, S3, S4 and S7's unreviewed line; T4): an ordered list of cards by position, each with its
// chapter, type, text (plain text: it may come from the model), options named correct or incorrect, its review marked
// by a badge's text and icon (not colour alone) and where it came from; above them the count against the blueprint.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;

beforeEach(() => {
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

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

const questionList = () => screen.getByRole('list', { name: t('editorial.test.questions.title') });
/** The question cards, the list's own items (each card holds a list of its options too). */
const cards = () => [...questionList().querySelectorAll(':scope > li')].map((card) => card as HTMLElement);
const card = (index: number) => {
  const found = cards()[index];
  expect(found).toBeDefined();
  return found ?? document.body;
};
/** A card's options, the items of the list inside it. */
const options = (of: HTMLElement) => within(within(of).getByRole('list')).getAllByRole('listitem');

/** The icon beside a badge's text: in the element that holds the text, or the one around it. */
const iconOf = (text: HTMLElement) => text.querySelector('svg') ?? text.parentElement?.querySelector('svg') ?? null;

const asked = (question: Partial<EditorialQuestionDto>) => ({ ...unreviewedAi, ...question });

describe('the question list', () => {
  test('lists the questions by position, each with its position, chapter, type and text', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 2, name: t('editorial.test.questions.title') })).toBeInTheDocument();
    expect(questionList().tagName).toBe('OL');
    expect(cards()).toHaveLength(3);
    for (const [index, question, chapter] of [
      [0, approvedAi, 'Einleitung'],
      [1, unreviewedAi, 'Einleitung'],
      [2, handWritten, 'Handout'],
    ] as const) {
      expect(card(index)).toHaveTextContent(t('editorial.test.questions.position', { position: question.position }));
      expect(card(index)).toHaveTextContent(t('editorial.test.questions.chapter', { title: chapter }));
      expect(card(index)).toHaveTextContent(t(`glossary.questionType.${question.type}`));
      expect(card(index)).toHaveTextContent(question.text);
    }
  });

  test('speaks German by default', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 2, name: 'Fragen' })).toBeInTheDocument();
    expect(card(0)).toHaveTextContent('Frage 1');
    expect(card(0)).toHaveTextContent('Kapitel: Einleitung');
    expect(card(0)).toHaveTextContent('Single Choice');
    expect(card(1)).toHaveTextContent('Multiple Choice');
    expect(card(2)).toHaveTextContent('Kapitel: Handout');
  });

  test('switched to English, speaks English', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });

    expect(await screen.findByRole('heading', { level: 2, name: 'Questions' })).toBeInTheDocument();
    expect(card(0)).toHaveTextContent('Question 1');
    expect(card(0)).toHaveTextContent('Chapter: Einleitung');
    expect(card(0)).toHaveTextContent('Single choice');
    expect(card(1)).toHaveTextContent('Multiple choice');
  });

  test('a question about a chapter the version does not have names it unknown', async () => {
    version = versionOf('Draft', { questions: [asked({ chapterId: '0198c1a0-0000-7000-8000-0000000000ef' })] });
    await renderPage();

    expect(card(0)).toHaveTextContent(t('editorial.test.questions.unknownChapter'));
    expect(card(0)).toHaveTextContent('Kapitel: unbekannt');
  });

  test('an unreviewed question is marked Ungeprüft and an approved one Geprüft, by a badge with an icon', async () => {
    await renderPage();

    const approvedBadge = within(card(0)).getByText(t('glossary.questionReview.Approved'));
    expect(approvedBadge).toHaveTextContent('Geprüft');
    expect(iconOf(approvedBadge)).not.toBeNull();
    expect(within(card(0)).queryByText(t('glossary.questionReview.Unreviewed'))).not.toBeInTheDocument();
    for (const index of [1, 2]) {
      const badge = within(card(index)).getByText('Ungeprüft');
      expect(badge).toHaveTextContent(t('glossary.questionReview.Unreviewed'));
      expect(iconOf(badge)).not.toBeNull();
      expect(within(card(index)).queryByText(t('glossary.questionReview.Approved'))).not.toBeInTheDocument();
    }
  });

  test('the review badges speak English when switched', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });
    // card() finds the list by its English name, so wait for the page to have re-rendered in English first.
    await screen.findByRole('heading', { level: 2, name: 'Questions' });

    expect(await within(card(0)).findByText('Approved')).toBeInTheDocument();
    expect(within(card(1)).getByText('Unreviewed')).toBeInTheDocument();
  });

  test('an AI-drafted question says KI-entworfen, noch nicht geprüft, and a hand-written one Von Hand geschrieben', async () => {
    await renderPage();

    expect(within(card(1)).getByText('KI-entworfen, noch nicht geprüft')).toBeInTheDocument();
    expect(
      within(card(1)).getByText(
        t('editorial.test.questions.provenance.unreviewed', { origin: t('glossary.questionProvenance.AiDrafted') }),
      ),
    ).toBeInTheDocument();
    expect(within(card(2)).getByText('Von Hand geschrieben, noch nicht geprüft')).toBeInTheDocument();
    // An approved question is not said to be unreviewed; who approved it, and when, is T8's.
    expect(within(card(0)).queryByText(/noch nicht geprüft/)).not.toBeInTheDocument();
  });

  test('the provenance speaks English when switched', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });
    // card() finds the list by its English name, so wait for the page to have re-rendered in English first.
    await screen.findByRole('heading', { level: 2, name: 'Questions' });

    expect(await within(card(1)).findByText('AI-drafted, not yet approved')).toBeInTheDocument();
    expect(within(card(2)).getByText('Written by hand, not yet approved')).toBeInTheDocument();
  });

  test('the correct options are named as correct, not by colour alone', async () => {
    await renderPage();

    const items = options(card(1));
    expect(items).toHaveLength(3);
    for (const [index, text, correct] of [
      [0, 'Seife', true],
      [1, 'Desinfektionsmittel', true],
      [2, 'Kaltes Wasser allein', false],
    ] as const) {
      const option = within(items[index] ?? document.body);
      expect(option.getByText(text)).toBeInTheDocument();
      const named = option.getByText(
        t(correct ? 'editorial.test.questions.correct' : 'editorial.test.questions.incorrect'),
      );
      expect(named).toHaveClass('sr-only');
      expect(named).toHaveTextContent(correct ? 'Richtig' : 'Falsch');
      expect(
        option.queryByText(t(correct ? 'editorial.test.questions.incorrect' : 'editorial.test.questions.correct')),
      ).toBeNull();
    }
    // A correct option carries a check mark besides its name.
    expect(items[0]?.querySelector('svg')).not.toBeNull();
  });

  test('a question’s text with markup is shown as text', async () => {
    const text = '<img src=x onerror="window.hacked = true">\nIst **das** wichtig?';
    version = versionOf('Draft', {
      questions: [
        asked({
          text,
          options: [
            { id: '0198c1a0-0000-7000-8000-0000000000b8', text: '<b>fett</b>', correct: true },
            { id: '0198c1a0-0000-7000-8000-0000000000b9', text: '[Link](javascript:alert(1))', correct: false },
          ],
        }),
      ],
    });
    const { container } = await renderPage();

    expect(container.querySelector('img, b, strong, a[href^="javascript"]')).toBeNull();
    const shownText = [...card(0).querySelectorAll('.whitespace-pre-line')].find(
      (element) => element.textContent === text,
    );
    expect(shownText).toBeDefined();
    expect(within(card(0)).getByText('<b>fett</b>')).toBeInTheDocument();
    expect(within(card(0)).getByText('[Link](javascript:alert(1))')).toBeInTheDocument();
  });
});

describe('the count against the blueprint', () => {
  /** The summary line that says the text exactly; it is read with the page, not announced. */
  const line = (text: string) => {
    const found = screen.getByText(text);
    expect(found.closest('.tabular-nums')).not.toBeNull();
    expect(found.closest('[role="status"], [role="alert"]')).toBeNull();
    return found;
  };

  test('the summary says 1 von 10 Fragen geprüft, the questions held and the ones still unreviewed', async () => {
    await renderPage();

    expect(line(t('editorial.test.summary.reviewed', { reviewed: 1, count: 10 }, 10))).toBeInTheDocument();
    expect(line('1 von 10 Fragen geprüft')).toBeInTheDocument();
    expect(line('Der Test hat 3 von 10 verlangten Fragen.')).toBeInTheDocument();
    expect(line('2 Fragen sind noch ungeprüft.')).toBeInTheDocument();
    expect(screen.queryByText(/Entfernen Sie/)).not.toBeInTheDocument();
    expect(screen.queryByText('Alle Fragen sind geprüft.')).not.toBeInTheDocument();
  });

  test('the summary speaks English when switched: 1 of 10 questions approved', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });

    expect(await screen.findByText('1 of 10 questions approved')).toBeInTheDocument();
    expect(screen.getByText('The test has 3 of the 10 questions required.')).toBeInTheDocument();
    expect(screen.getByText('2 questions are still unreviewed.')).toBeInTheDocument();
  });

  test('a test with more questions than the blueprint says how many to remove', async () => {
    version = versionOf('Draft', { blueprint: { questions: 2, passingScorePercent: 80, validityMonths: 12 } });
    await renderPage();

    expect(line('1 von 2 Fragen geprüft')).toBeInTheDocument();
    expect(line('Der Test hat 3 von 2 verlangten Fragen.')).toBeInTheDocument();
    expect(line('Entfernen Sie eine Frage, damit der Test den Vorgaben entspricht.')).toBeInTheDocument();
  });

  test('a test of three against a blueprint of one says to remove 2, in German and English', async () => {
    version = versionOf('Draft', { blueprint: { questions: 1, passingScorePercent: 80, validityMonths: 12 } });
    await renderPage();

    expect(line('1 von einer Frage geprüft')).toBeInTheDocument();
    expect(line('Der Test hat 3 von einer verlangten Frage.')).toBeInTheDocument();
    expect(line(t('editorial.test.summary.tooMany', { count: 2 }, 2))).toBeInTheDocument();
    expect(line('Entfernen Sie 2 Fragen, damit der Test den Vorgaben entspricht.')).toBeInTheDocument();

    setLanguage('en-GB', { remember: false });

    expect(await screen.findByText('Remove 2 questions to match the blueprint.')).toBeInTheDocument();
    expect(screen.getByText('The test has 3 of the one question required.')).toBeInTheDocument();
    expect(screen.getByText('1 of one question approved')).toBeInTheDocument();
  });

  test('a test that holds what the blueprint requires says nothing about the count held', async () => {
    version = versionOf('Draft', { blueprint: { questions: 3, passingScorePercent: 80, validityMonths: 12 } });
    await renderPage();

    expect(line('1 von 3 Fragen geprüft')).toBeInTheDocument();
    expect(screen.queryByText(/Der Test hat/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Entfernen Sie/)).not.toBeInTheDocument();
  });

  test('one unreviewed question is said in the singular', async () => {
    version = versionOf('Draft', { questions: [approvedAi, unreviewedAi] });
    await renderPage();

    expect(line('Eine Frage ist noch ungeprüft.')).toBeInTheDocument();
  });

  test('a test whose questions are all approved says so', async () => {
    version = versionOf('Draft', {
      questions: [approvedAi, { ...unreviewedAi, review: 'Approved' }, { ...handWritten, review: 'Approved' }],
    });
    await renderPage();

    expect(line('3 von 10 Fragen geprüft')).toBeInTheDocument();
    expect(line(t('editorial.test.summary.allReviewed'))).toBeInTheDocument();
    expect(line('Alle Fragen sind geprüft.')).toBeInTheDocument();
    expect(screen.queryByText(/noch ungeprüft/)).not.toBeInTheDocument();
  });
});

describe('an empty test', () => {
  test('an empty test says so and offers to add a question', async () => {
    version = versionOf('Draft', { questions: [] });
    await renderPage();

    const sentence = await screen.findByText(t('editorial.test.questions.empty'));
    expect(sentence).toHaveTextContent('Dieser Test hat noch keine Fragen.');
    const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
    expect(emptyState.getByRole('button', { name: t('editorial.test.questions.add') })).toBeEnabled();
    expect(emptyState.getByRole('button', { name: 'Frage hinzufügen' })).toBeInTheDocument();
    expect(screen.getByText('0 von 10 Fragen geprüft')).toBeInTheDocument();
    expect(screen.getByText('Der Test hat 0 von 10 verlangten Fragen.')).toBeInTheDocument();
    // With no question there is none to review, nor one that is reviewed.
    expect(screen.queryByText(t('editorial.test.summary.allReviewed'))).not.toBeInTheDocument();
    expect(screen.queryByText(/noch ungeprüft/)).not.toBeInTheDocument();
  });

  test('a draft without chapters says to add one first, and offers no question to add', async () => {
    version = versionOf('Draft', { chapters: [], questions: [] });
    await renderPage();

    expect(await screen.findByText(t('editorial.test.questions.noChapters'))).toBeVisible();
    expect(
      screen.getByText('Jede Frage bezieht sich auf ein Kapitel. Legen Sie zuerst ein Kapitel an.'),
    ).toBeInTheDocument();
    expect(screen.getByText(t('editorial.test.questions.empty'))).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('editorial.test.questions.add') })).not.toBeInTheDocument();
  });

  test('a published version without questions says so and offers nothing to add', async () => {
    version = versionOf('Published', { chapters: [introduction], questions: [] });
    await renderPage();

    expect(await screen.findByText(t('editorial.test.questions.empty'))).toBeVisible();
    expect(screen.queryByRole('button', { name: t('editorial.test.questions.add') })).not.toBeInTheDocument();
    expect(screen.queryByText(t('editorial.test.questions.noChapters'))).not.toBeInTheDocument();
  });
});

test('a published version shows its questions, marked, as a draft does', async () => {
  version = versionOf('Published');
  await renderPage();

  expect(cards()).toHaveLength(3);
  expect(within(card(0)).getByText('Geprüft')).toBeInTheDocument();
  expect(within(card(1)).getByText('Ungeprüft')).toBeInTheDocument();
  expect(screen.getByText('1 von 10 Fragen geprüft')).toBeInTheDocument();
});
