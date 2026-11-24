import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { type AssessmentExaminationDto, getGetAssessmentExaminationsBySessionSessionIdQueryKey } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import {
  courseTitle,
  courseVersion,
  examination,
  hygienePlan,
  introduction,
  refusal,
  result,
  sessionId,
  unknownChapterId,
  versionId,
} from './examinationFixtures';
import {
  type ExaminationHost,
  type Fetch,
  hostAnswering,
  notifyAtOnce,
  renderAt,
  resultAddress,
  routed,
} from './examinationHost';
import ExaminationResultPage from './ExaminationResultPage.vue';

// The result page (#48) at /assessment/sessions/:sessionId/result: it reads the examination by session and shows its
// lastResult, so a reload shows it again; the catalog's version gives the course's title and the chapters' titles.
// Passed or failed, the score and the pass mark as percentages (n(x / 100, 'percent')), the attempt's number and the
// attempts left; after a failure the chapters to repeat by title, each linked to the player's chapter, and the lock
// once every attempt is used (ADR 0023/0025).
const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let answers: ExaminationHost;
function answering(read: AssessmentExaminationDto) {
  answers = hostAnswering(read);
}

beforeEach(() => {
  answering(examination({ attemptsUsed: 1, lastResult: result() }));
  host.mockImplementation((input, init) => routed(answers)(input, init));
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const renderPage = () => renderAt(ExaminationResultPage, resultAddress);
const heading = () =>
  screen.findByRole('heading', { level: 1, name: t('assessment.result.heading', { title: courseTitle }) });
/** A percentage as the language writes it; toHaveTextContent folds the received U+00A0, not the expected's. */
const percent = (value: number) => i18n.global.n(value / 100, 'percent');
const folded = (text: string) => text.replace(/\s+/g, ' ');
const scoreText = (score: number, passing: number) =>
  folded(t('assessment.result.score', { score: percent(score), passing: percent(passing) }));
const chapterHref = (chapterId: string) => `/learning/courses/${versionId}?chapter=${chapterId}`;

/** The list of chapters to repeat: an ordered list after the text that introduces it. */
const chapterList = () => {
  const list = screen.getAllByRole('list').find((candidate) => candidate.tagName === 'OL');
  if (list === undefined) {
    throw new Error('No ordered list of chapters.');
  }
  return list;
};

test("a passed attempt is headed by the course's title, which takes the focus, and says Bestanden with both percentages", async () => {
  answering(
    examination({
      status: 'Passed',
      attemptsUsed: 1,
      lastResult: result({ outcome: 'Passed', scorePercent: 100, chaptersToRepeat: [], attemptsLeft: 2 }),
    }),
  );
  await renderPage();

  const h1 = await heading();
  await waitFor(() => expect(h1).toHaveFocus());
  expect(screen.getByText(t('assessment.result.outcome.Passed'))).toBeInTheDocument();
  expect(document.body).toHaveTextContent(scoreText(100, 80));
  expect(screen.getByText(t('assessment.result.attempt', { number: 1 }))).toBeInTheDocument();
  expect(screen.getByText(t('assessment.result.passedNext'))).toBeInTheDocument();
  expect(screen.getByRole('link', { name: t('assessment.test.toCourses') })).toHaveAttribute('href', '/learning');
  // Passed: no attempts left to count, no chapters to repeat, no next attempt.
  expect(screen.queryByText(t('assessment.result.attemptsLeft', { count: 2 }, 2))).toBeNull();
  expect(screen.queryByText(t('assessment.result.repeatRecommended'))).toBeNull();
  expect(screen.queryByRole('link', { name: t('assessment.result.retry') })).toBeNull();
});

test('a failed attempt with attempts left says Nicht bestanden, how many are left, the chapters to repeat and the next attempt', async () => {
  answering(examination({ status: 'Unlocked', attemptsUsed: 1, lastResult: result({ attemptsLeft: 2 }) }));
  await renderPage();
  await heading();

  expect(screen.getByText(t('assessment.result.outcome.Failed'))).toBeInTheDocument();
  expect(document.body).toHaveTextContent(scoreText(50, 80));
  expect(screen.getByText(t('assessment.result.attempt', { number: 1 }))).toBeInTheDocument();
  expect(screen.getByText(t('assessment.result.attemptsLeft', { count: 2 }, 2))).toBeInTheDocument();
  expect(screen.getByText(t('assessment.result.repeatRecommended'))).toBeInTheDocument();
  expect(screen.getByRole('link', { name: t('assessment.result.retry') })).toHaveAttribute(
    'href',
    `/assessment/sessions/${sessionId}`,
  );
  expect(screen.queryByText(t('assessment.result.locked'))).toBeNull();
});

test('one attempt left is said in the singular', async () => {
  answering(examination({ status: 'Unlocked', attemptsUsed: 2, lastResult: result({ number: 2, attemptsLeft: 1 }) }));
  await renderPage();
  await heading();

  expect(screen.getByText('Noch ein Versuch')).toBeInTheDocument();
  expect(screen.getByText(t('assessment.result.attempt', { number: 2 }))).toBeInTheDocument();
});

test('the chapters to repeat are listed in the course order by title, each linked to its chapter in the player', async () => {
  // The result names them in another order than the course's.
  answering(examination({ status: 'Unlocked', attemptsUsed: 1, lastResult: result() }));
  await renderPage();
  await heading();

  const items = within(chapterList()).getAllByRole('listitem');
  expect(items.map((item) => item.textContent?.trim())).toEqual([introduction.title, hygienePlan.title]);
  expect(within(chapterList()).getByRole('link', { name: introduction.title })).toHaveAttribute(
    'href',
    chapterHref(introduction.chapterId),
  );
  expect(within(chapterList()).getByRole('link', { name: hygienePlan.title })).toHaveAttribute(
    'href',
    chapterHref(hygienePlan.chapterId),
  );
});

test('a locked test (every attempt used) says so and lists the chapters to work through again, without a next attempt', async () => {
  answering(
    examination({
      status: 'Exhausted',
      attemptsUsed: 3,
      lastResult: result({ number: 3, attemptsLeft: 0, chaptersToRepeat: [introduction.chapterId] }),
    }),
  );
  await renderPage();
  await heading();

  expect(screen.getByText(t('assessment.result.outcome.Failed'))).toBeInTheDocument();
  expect(screen.getByText(t('assessment.result.locked'))).toBeInTheDocument();
  expect(within(chapterList()).getByRole('link', { name: introduction.title })).toHaveAttribute(
    'href',
    chapterHref(introduction.chapterId),
  );
  expect(screen.queryByRole('link', { name: t('assessment.result.retry') })).toBeNull();
  expect(screen.queryByText(t('assessment.result.repeatRecommended'))).toBeNull();
  expect(screen.queryByText(/Noch .*Versuch/)).toBeNull();
});

test('a chapter the catalog does not name is listed as a chapter of the course, without a link', async () => {
  answering(
    examination({
      status: 'Unlocked',
      attemptsUsed: 1,
      lastResult: result({ chaptersToRepeat: [introduction.chapterId, unknownChapterId] }),
    }),
  );
  await renderPage();
  await heading();

  const unknown = within(chapterList()).getByText(t('assessment.result.unknownChapter'));
  expect(unknown.closest('a')).toBeNull();
  expect(within(chapterList()).getAllByRole('link')).toHaveLength(1);
});

test('when the catalog fails an alert with Retry takes the list’s place, the score stays, and Retry lists the chapters', async () => {
  const user = userEvent.setup();
  answers.catalogReading = async () => new Response('', { status: 503 });
  await renderPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('assessment.result.chaptersFailed'));
  expect(screen.getByText(t('assessment.result.outcome.Failed'))).toBeInTheDocument();
  expect(document.body).toHaveTextContent(scoreText(50, 80));

  answers.catalogReading = async () => Response.json(courseVersion());
  await user.click(within(alert).getByRole('button', { name: t('shell.retry') }));

  expect(await screen.findByRole('link', { name: introduction.title })).toHaveAttribute(
    'href',
    chapterHref(introduction.chapterId),
  );
  expect(screen.queryByRole('alert')).toBeNull();
});

test('without a submitted attempt the page says so and links to the test', async () => {
  answering(examination());
  await renderPage();

  expect(await screen.findByText(t('assessment.result.none'))).toBeInTheDocument();
  expect(screen.getByRole('link', { name: t('assessment.result.toTest') })).toHaveAttribute(
    'href',
    `/assessment/sessions/${sessionId}`,
  );
});

test('without an examination yet (404) the page says the test is not unlocked yet', async () => {
  answers.reading = async () => refusal(404, 'assessment.examination-not-found');
  await renderPage();

  expect(await screen.findByText(t('assessment.test.notUnlocked'))).toBeInTheDocument();
});

test('while the result loads the page is busy and shows skeletons', async () => {
  answers.reading = () => new Promise<Response>(() => undefined);
  const { container } = await renderPage();

  await waitFor(() => expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0));
  expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
});

test('a read that fails says so in an alert, and Retry reads it again', async () => {
  const user = userEvent.setup();
  const read = answers.reading;
  answers.reading = async () => new Response('', { status: 503 });
  await renderPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('assessment.result.loadFailed'));

  answers.reading = read;
  await user.click(within(alert).getByRole('button', { name: t('shell.retry') }));

  const h1 = await heading();
  await waitFor(() => expect(h1).toHaveFocus());
});

test('a session id that is not an id is never sent, and the page says the test does not exist', async () => {
  await renderAt(ExaminationResultPage, '/assessment/sessions/not-a-session/result');

  expect(await screen.findByRole('alert')).toHaveTextContent(t('assessment.test.notFound'));
  expect(host).not.toHaveBeenCalled();
});

describe('in English', () => {
  test('the result says Failed, the percentages as the English write them, and the attempts left', async () => {
    setLanguage('en-GB', { remember: false });
    answering(examination({ status: 'Unlocked', attemptsUsed: 1, lastResult: result({ attemptsLeft: 2 }) }));
    await renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: `Result: ${courseTitle}` })).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(document.body).toHaveTextContent(folded(`Your score: ${percent(50)}; needed to pass: ${percent(80)}`));
    expect(screen.getByText('2 attempts left')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Start the next attempt' })).toBeInTheDocument();
  });
});

test('a read again that fails while the result is shown keeps the result, without an alert', async () => {
  notifyAtOnce();
  const { queryClient } = await renderPage();
  await heading();
  expect(screen.getByText(t('assessment.result.outcome.Failed'))).toBeInTheDocument();
  answers.reading = async () => new Response('', { status: 503 });
  const examinationKey = getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId);

  await queryClient.refetchQueries({ queryKey: examinationKey });

  expect(queryClient.getQueryState(examinationKey)?.status).toBe('error');
  expect(screen.getByText(t('assessment.result.outcome.Failed'))).toBeInTheDocument();
  expect(screen.queryByText(t('assessment.result.loadFailed'))).toBeNull();
  expect(screen.queryByRole('button', { name: t('shell.retry') })).toBeNull();
});
