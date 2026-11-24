import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import { courseTitle, examination, inAttempt, refusal, result, sessionId, unknownRefusal } from './examinationFixtures';
import {
  type ExaminationHost,
  type Fetch,
  hostAnswering,
  readPath,
  renderAt,
  requestsOf,
  routed,
  startPath,
  testAddress,
} from './examinationHost';
import ExaminationPage from './ExaminationPage.vue';
import ExaminationResultPage from './ExaminationResultPage.vue';

// The learner's test page (#48) at /assessment/sessions/:sessionId: it finds the examination by Learning's session id
// (GET /assessment/examinations/by-session/{sessionId}, ADR 0010/0012), says while it is not unlocked yet (and asks
// again every 3 s, since the examination opens a few seconds after ContentCompletedV1), offers the start of an attempt
// with "attempt n of max", and says when the test is passed or locked. Data through useExamination(sessionId).
const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let answers: ExaminationHost;
beforeEach(() => {
  answers = hostAnswering(examination());
  host.mockImplementation((input, init) => routed(answers)(input, init));
});
afterEach(() => {
  host.mockReset();
  vi.useRealTimers();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const requests = () => requestsOf(host.mock.calls);
const reads = () => requests().filter(({ method, path }) => method === 'GET' && path === readPath);
const starts = () => requests().filter(({ method, path }) => method === 'POST' && path === startPath);

const renderPage = (address = testAddress) => renderAt(ExaminationPage, address);
const startButton = () => screen.findByRole('button', { name: t('assessment.test.start') });

test('the test page and the result page are the details at /assessment/sessions/:sessionId and …/result', () => {
  const router = createAppRouter(createMemoryHistory());

  expect(router.resolve(testAddress).matched.at(-1)?.components?.default).toBe(ExaminationPage);
  expect(router.resolve(`${testAddress}/result`).matched.at(-1)?.components?.default).toBe(ExaminationResultPage);
});

test("the page reads the examination by the address's session id", async () => {
  await renderPage();
  await startButton();

  expect(reads()).toEqual([{ method: 'GET', path: readPath, body: undefined }]);
});

test('a session id that is not an id is never sent, and the page says the test does not exist', async () => {
  await renderPage('/assessment/sessions/not-a-session');

  expect(await screen.findByRole('alert')).toHaveTextContent(t('assessment.test.notFound'));
  expect(requests().filter(({ path }) => path.startsWith('/assessment/'))).toEqual([]);
});

test('while the examination loads the page is busy and shows skeletons shaped like its heading and three questions', async () => {
  answers.reading = () => new Promise<Response>(() => undefined);
  const { container } = await renderPage();

  await waitFor(() => expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThanOrEqual(4));
  expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
});

describe('not unlocked yet', () => {
  test('a locked examination says the test is not unlocked yet and links to the courses', async () => {
    answers = hostAnswering(examination({ status: 'Locked' }));
    await renderPage();

    expect(await screen.findByText(t('assessment.test.notUnlocked'))).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t('assessment.test.toCourses') })).toHaveAttribute('href', '/learning');
    expect(screen.queryByRole('button', { name: t('assessment.test.start') })).toBeNull();
  });

  test('without an examination yet (404 not-found) the page says so, asks again every 3 s, and stops once it is there', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    answers.reading = async () => refusal(404, 'assessment.examination-not-found');
    await renderPage();

    expect(await screen.findByText(t('assessment.test.notUnlocked'))).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t('assessment.test.toCourses') })).toHaveAttribute('href', '/learning');
    // A 404 is not retried: one read, then the next only after 3 s.
    expect(reads()).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(2_900);
    expect(reads()).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(100);
    await vi.waitFor(() => expect(reads()).toHaveLength(2));

    answers.reading = async () => Response.json(examination());
    await vi.advanceTimersByTimeAsync(3_000);
    await startButton();
    const once = reads().length;

    await vi.advanceTimersByTimeAsync(9_000);
    expect(reads()).toHaveLength(once);
  });
});

describe('errors', () => {
  test.each([
    ['the read fails', 'assessment.test.loadFailed', async () => new Response('', { status: 503 })],
    ['the network fails', 'assessment.test.loadFailed', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])(
    'when %s the page says so in an alert, and Retry reads again and focuses the heading',
    async (_case, key, fail) => {
      answers.reading = fail;
      await renderPage();

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent(t(key));
      const before = reads().length;

      answers.reading = async () => Response.json(inAttempt());
      await userEvent.click(within(alert).getByRole('button', { name: t('shell.retry') }));

      const h1 = await screen.findByRole('heading', { level: 1 });
      await waitFor(() => expect(h1).toHaveFocus());
      expect(reads().length).toBeGreaterThan(before);
      expect(screen.queryByRole('alert')).toBeNull();
    },
  );

  test('an incomplete identity says so, without Retry', async () => {
    answers.reading = async () => refusal(403, 'assessment.identity-incomplete');
    await renderPage();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t('assessment.test.noIdentity'));
    expect(within(alert).queryByRole('button')).toBeNull();
  });

  test('the page picks its words by the code, never by what the host wrote', async () => {
    answers.reading = async () => refusal(403, 'assessment.identity-incomplete');
    await renderPage();

    expect(await screen.findByRole('alert')).not.toHaveTextContent('Not for the user.');
  });
});

describe('unlocked, before the start', () => {
  test('the page says only which attempt of how many comes next, and offers the start', async () => {
    answers = hostAnswering(examination({ attemptsUsed: 1, maxAttempts: 3 }));
    await renderPage();

    expect(await startButton()).toBeEnabled();
    expect(screen.getByText(t('assessment.test.intro', { number: 2, max: 3 }))).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: t('assessment.test.toResult') })).toBeNull();
    expect(screen.queryAllByRole('group')).toEqual([]);
  });

  test('after an attempt the page links to its result', async () => {
    answers = hostAnswering(examination({ attemptsUsed: 1, lastResult: result() }));
    await renderPage();

    await startButton();
    expect(screen.getByRole('link', { name: t('assessment.test.toResult') })).toHaveAttribute(
      'href',
      `/assessment/sessions/${sessionId}/result`,
    );
  });

  test('without a test definition yet (maxAttempts null) the page says the test is being prepared and the start is off', async () => {
    answers = hostAnswering(examination({ maxAttempts: null }));
    await renderPage();

    expect(await startButton()).toBeDisabled();
    expect(screen.getByText(t('assessment.test.notReady'))).toBeInTheDocument();
  });

  test('the start posts an attempt once, reads the examination again, and moves the focus to the questions', async () => {
    const user = userEvent.setup();
    await renderPage();
    const before = reads().length;
    answers.reading = async () => Response.json(inAttempt());

    await user.click(await startButton());

    const questions = await screen.findByRole('heading', { level: 2, name: t('assessment.test.questions') });
    await waitFor(() => expect(questions).toHaveFocus());
    expect(questions).toHaveAttribute('tabindex', '-1');
    expect(starts()).toEqual([{ method: 'POST', path: startPath, body: undefined }]);
    expect(reads().length).toBeGreaterThan(before);
    expect(screen.getAllByRole('group')).toHaveLength(2);
  });

  test.each([
    ['test-definition-missing', 'assessment.test.notReady', () => refusal(409, 'assessment.test-definition-missing')],
    ['examination-changed', 'assessment.test.changed', () => refusal(409, 'assessment.examination-changed')],
    ['a 500', 'assessment.test.startFailed', () => new Response('', { status: 500 })],
  ])('a start refused with %s says so in an alert, and Retry posts again', async (_case, key, refuse) => {
    const user = userEvent.setup();
    answers.starting = async () => refuse();
    await renderPage();

    await user.click(await startButton());
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t(key));

    answers.starting = hostAnswering().starting;
    answers.reading = async () => Response.json(inAttempt());
    await user.click(within(alert).getByRole('button', { name: t('shell.retry') }));

    await screen.findByRole('heading', { level: 2, name: t('assessment.test.questions') });
    expect(starts()).toHaveLength(2);
  });

  test('a start refused with an unknown module code says what the host said', async () => {
    const user = userEvent.setup();
    answers.starting = async () => unknownRefusal(409, 'Der Test ist gerade gesperrt.');
    await renderPage();

    await user.click(await startButton());

    expect(await screen.findByRole('alert')).toHaveTextContent('Der Test ist gerade gesperrt.');
  });

  test('a start refused as not unlocked (an attempt opened elsewhere) reads the examination again', async () => {
    const user = userEvent.setup();
    answers.starting = async () => refusal(409, 'assessment.examination-not-unlocked');
    await renderPage();
    const before = reads().length;
    answers.reading = async () => Response.json(inAttempt());

    await user.click(await startButton());

    await waitFor(() => expect(reads().length).toBeGreaterThan(before));
    expect(await screen.findAllByRole('group')).toHaveLength(2);
  });
});

describe('in an attempt', () => {
  test("the page is headed by the course's title from the catalog", async () => {
    answers = hostAnswering(inAttempt());
    await renderPage();

    expect(
      await screen.findByRole('heading', { level: 1, name: t('assessment.test.heading', { title: courseTitle }) }),
    ).toBeInTheDocument();
  });

  test('while the catalog fails the page is headed "Test", and the questions are there all the same', async () => {
    answers = hostAnswering(inAttempt());
    answers.catalogReading = async () => new Response('', { status: 503 });
    await renderPage();

    expect(
      await screen.findByRole('heading', { level: 1, name: t('assessment.test.headingFallback') }),
    ).toBeInTheDocument();
    expect(await screen.findAllByRole('group')).toHaveLength(2);
  });
});

describe('finished', () => {
  test.each([
    ['Passed', 'assessment.test.passed', result({ outcome: 'Passed', scorePercent: 100, chaptersToRepeat: [] })],
    ['Exhausted', 'assessment.test.exhausted', result({ attemptsLeft: 0 })],
  ] as const)('a %s examination says so and links to the result, without a start', async (status, key, last) => {
    answers = hostAnswering(examination({ status, attemptsUsed: 3, lastResult: last }));
    await renderPage();

    expect(await screen.findByText(t(key))).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t('assessment.test.toResult') })).toHaveAttribute(
      'href',
      `/assessment/sessions/${sessionId}/result`,
    );
    expect(screen.queryByRole('button', { name: t('assessment.test.start') })).toBeNull();
    expect(screen.queryAllByRole('group')).toEqual([]);
  });
});
