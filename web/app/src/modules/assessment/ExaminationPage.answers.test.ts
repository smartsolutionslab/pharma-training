import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, onTestFinished, test, vi } from 'vitest';
import { getGetAssessmentExaminationsBySessionSessionIdQueryKey } from '@/api/generated';
import type { AssessmentAttemptQuestionDto, AssessmentExaminationDto } from '@/api/generated';
import { t } from '@/i18n';
import {
  chosen,
  coat,
  deferred,
  durationQuestion,
  examination,
  fiveSeconds,
  gloves,
  inAttempt,
  protectionQuestion,
  refusal,
  result,
  saved,
  sessionId,
  thirtySeconds,
} from './examinationFixtures';
import {
  answerPath,
  type ExaminationHost,
  type Fetch,
  hostAnswering,
  notifyAtOnce,
  readPath,
  renderAt,
  requestsOf,
  routed,
  testAddress,
} from './examinationHost';
import ExaminationPage from './ExaminationPage.vue';

// Answering on the test page (#48): every question of the attempt on one page, in the order the server sent them
// (ADR 0022), each a fieldset whose legend is "Frage n von N" and its text, with radios for single choice and
// checkboxes for multiple choice. Each change is saved at once (useAnswerSaver); the question says "Wird gespeichert …"
// then "Gespeichert", and the page's one polite status region says which answer was saved. The server is the only
// record: a reload shows the answers it holds, and nothing goes to the browser's storage.
const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let answers: ExaminationHost;
/** What the host holds: the examination with each saved answer. */
let stored: AssessmentExaminationDto;

function storing(examinationRead: AssessmentExaminationDto) {
  stored = examinationRead;
  answers = hostAnswering(stored);
  answers.reading = async () => Response.json(stored);
  answers.answering = async (questionId, optionIds) => {
    const attempt = stored.currentAttempt;
    if (attempt !== null) {
      stored = {
        ...stored,
        currentAttempt: {
          ...attempt,
          questions: attempt.questions.map((question): AssessmentAttemptQuestionDto =>
            question.id === questionId ? chosen(question, optionIds) : question,
          ),
        },
      };
    }
    return saved();
  };
}

beforeEach(() => {
  storing(inAttempt());
  host.mockImplementation((input, init) => routed(answers)(input, init));
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const puts = () => requestsOf(host.mock.calls).filter(({ method }) => method === 'PUT');
const renderPage = () => renderAt(ExaminationPage, testAddress);

/** The question's fieldset, by its legend's "Frage n von N". */
async function questionGroup(number: number, total = 2) {
  const label = t('assessment.test.question', { number, total });
  const groups = await screen.findAllByRole('group');
  const group = groups.find((candidate) => candidate.querySelector('legend')?.textContent?.includes(label) === true);
  if (group === undefined) {
    throw new Error(`No fieldset is headed ${label}.`);
  }
  return group;
}

const statusRegions = () => screen.getAllByRole('status');

test('every question of the attempt is a fieldset on one page, in the order the server sent them', async () => {
  await renderPage();

  const groups = await screen.findAllByRole('group');
  expect(groups).toHaveLength(2);
  for (const group of groups) {
    expect(group.tagName).toBe('FIELDSET');
  }
  const first = await questionGroup(1);
  expect(first.querySelector('legend')).toHaveTextContent(durationQuestion.text);
  expect(groups[0]).toBe(first);
  const second = await questionGroup(2);
  expect(second.querySelector('legend')).toHaveTextContent(protectionQuestion.text);
  expect(groups[1]).toBe(second);
});

test('a single-choice question offers radios, a multiple-choice question checkboxes, each with its hint and the options as received', async () => {
  await renderPage();

  const single = await questionGroup(1);
  expect(
    within(single)
      .getAllByRole('radio')
      .map((radio) => radio.getAttribute('aria-label') ?? labelOf(radio)),
  ).toEqual(['5 Sekunden', '30 Sekunden']);
  expect(within(single).queryAllByRole('checkbox')).toEqual([]);
  expect(within(single).getByText(t('assessment.test.singleHint'))).toBeInTheDocument();

  const multiple = await questionGroup(2);
  expect(
    within(multiple)
      .getAllByRole('checkbox')
      .map((box) => box.getAttribute('aria-label') ?? labelOf(box)),
  ).toEqual(['Kittel', 'Sandalen', 'Handschuhe']);
  expect(within(multiple).queryAllByRole('radio')).toEqual([]);
  expect(within(multiple).getByText(t('assessment.test.multipleHint'))).toBeInTheDocument();
});

/** The text of the label an input is named by. */
function labelOf(input: HTMLElement): string {
  const labels = (input as HTMLInputElement).labels;
  return labels === null || labels.length === 0 ? '' : (labels[0]?.textContent?.trim() ?? '');
}

test('the page says how many questions are answered, in tabular figures, and counts a new answer', async () => {
  const user = userEvent.setup();
  await renderPage();

  const progress = await screen.findByText(t('assessment.test.answered', { answered: 0, total: 2 }));
  expect(progress).toHaveClass('tabular-nums');

  await user.click(within(await questionGroup(1)).getByRole('radio', { name: '30 Sekunden' }));

  expect(await screen.findByText(t('assessment.test.answered', { answered: 1, total: 2 }))).toBeInTheDocument();
});

test('the answers the server holds are chosen when the page opens', async () => {
  storing(inAttempt([chosen(durationQuestion, [thirtySeconds]), chosen(protectionQuestion, [coat, gloves])]));
  await renderPage();

  const single = await questionGroup(1);
  expect(within(single).getByRole('radio', { name: '30 Sekunden' })).toBeChecked();
  expect(within(single).getByRole('radio', { name: '5 Sekunden' })).not.toBeChecked();
  const multiple = await questionGroup(2);
  expect(within(multiple).getByRole('checkbox', { name: 'Kittel' })).toBeChecked();
  expect(within(multiple).getByRole('checkbox', { name: 'Handschuhe' })).toBeChecked();
  expect(within(multiple).getByRole('checkbox', { name: 'Sandalen' })).not.toBeChecked();
  expect(screen.getByText(t('assessment.test.answered', { answered: 2, total: 2 }))).toBeInTheDocument();
});

test('a reload shows the answer the server saved', async () => {
  const user = userEvent.setup();
  await renderPage();
  await user.click(within(await questionGroup(1)).getByRole('radio', { name: '5 Sekunden' }));
  await within(await questionGroup(1)).findByText(t('assessment.test.save.saved'));

  cleanup();
  await renderPage();

  expect(within(await questionGroup(1)).getByRole('radio', { name: '5 Sekunden' })).toBeChecked();
});

test('choosing a radio saves it at once, says Gespeichert in the question, and announces it once in the one status region', async () => {
  const user = userEvent.setup();
  const answer = deferred();
  answers.answering = () => answer.answer;
  await renderPage();
  const single = await questionGroup(1);

  await user.click(within(single).getByRole('radio', { name: '30 Sekunden' }));

  expect(await within(single).findByText(t('assessment.test.save.saving'))).toBeInTheDocument();
  expect(puts()).toEqual([
    { method: 'PUT', path: answerPath(durationQuestion.id), body: { optionIds: [thirtySeconds] } },
  ]);

  answer.release(saved());

  expect(await within(single).findByText(t('assessment.test.save.saved'))).toBeInTheDocument();
  const announcement = t('assessment.test.save.announced', { number: 1 });
  await waitFor(() => expect(statusRegions()).toHaveLength(1));
  const [region] = statusRegions();
  if (region === undefined) {
    throw new Error('No status region.');
  }
  await waitFor(() => expect(within(region).getAllByText(announcement)).toHaveLength(1));
  expect(region).toHaveAttribute('aria-live', 'polite');
  // The question's own Gespeichert is shown, not announced: the status region is the page's only one.
  expect(within(single).queryByRole('status')).toBeNull();
});

test('the status region says the latest save: after a second question is saved, it names that question', async () => {
  const user = userEvent.setup();
  await renderPage();

  await user.click(within(await questionGroup(1)).getByRole('radio', { name: '30 Sekunden' }));
  await within(await questionGroup(1)).findByText(t('assessment.test.save.saved'));
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));
  await within(await questionGroup(2)).findByText(t('assessment.test.save.saved'));

  await waitFor(() => expect(statusRegions()[0]).toHaveTextContent(t('assessment.test.save.announced', { number: 2 })));
});

test('checking options of a multiple-choice question saves the whole checked set each time', async () => {
  const user = userEvent.setup();
  await renderPage();
  const multiple = await questionGroup(2);

  await user.click(within(multiple).getByRole('checkbox', { name: 'Kittel' }));
  await waitFor(() => expect(puts()).toHaveLength(1));
  await user.click(within(multiple).getByRole('checkbox', { name: 'Handschuhe' }));

  await waitFor(() => expect(puts()).toHaveLength(2));
  expect(puts()[0]?.body).toEqual({ optionIds: [coat] });
  const latest = puts()[1]?.body as { optionIds: string[] };
  expect([...latest.optionIds].sort()).toEqual([coat, gloves].sort());
});

test('the latest choice wins: a change while a save is on its way is saved after it, and shown', async () => {
  const user = userEvent.setup();
  const first = deferred();
  let calls = 0;
  const storingAnswer = answers.answering;
  answers.answering = (questionId, optionIds) => {
    calls += 1;
    return calls === 1 ? first.answer : storingAnswer(questionId, optionIds);
  };
  await renderPage();
  const single = await questionGroup(1);

  await user.click(within(single).getByRole('radio', { name: '5 Sekunden' }));
  await waitFor(() => expect(puts()).toHaveLength(1));
  await user.click(within(single).getByRole('radio', { name: '30 Sekunden' }));
  expect(within(single).getByRole('radio', { name: '30 Sekunden' })).toBeChecked();
  first.release(saved());

  await waitFor(() => expect(puts()).toHaveLength(2));
  expect(puts().map(({ body }) => body)).toEqual([{ optionIds: [fiveSeconds] }, { optionIds: [thirtySeconds] }]);
  expect(await within(single).findByText(t('assessment.test.save.saved'))).toBeInTheDocument();
  expect(within(single).getByRole('radio', { name: '30 Sekunden' })).toBeChecked();
});

test('a failed save says so in an alert in its question, and Erneut speichern saves it again', async () => {
  const user = userEvent.setup();
  const storingAnswer = answers.answering;
  answers.answering = async () => new Response('', { status: 503 });
  await renderPage();
  const single = await questionGroup(1);

  await user.click(within(single).getByRole('radio', { name: '30 Sekunden' }));

  const alert = await within(single).findByRole('alert');
  expect(alert).toHaveTextContent(t('assessment.test.save.failed', { number: 1 }));
  // The choice stays shown: the draft is kept.
  expect(within(single).getByRole('radio', { name: '30 Sekunden' })).toBeChecked();

  answers.answering = storingAnswer;
  await user.click(within(single).getByRole('button', { name: t('assessment.test.save.retry') }));

  expect(await within(single).findByText(t('assessment.test.save.saved'))).toBeInTheDocument();
  expect(within(single).queryByRole('alert')).toBeNull();
  expect(puts()).toHaveLength(2);
});

test('an answer cannot be cleared: unchecking the last option sends nothing and the question says so', async () => {
  const user = userEvent.setup();
  storing(inAttempt([durationQuestion, chosen(protectionQuestion, [coat])]));
  await renderPage();
  const multiple = await questionGroup(2);

  await user.click(within(multiple).getByRole('checkbox', { name: 'Kittel' }));

  expect(await within(multiple).findByText(t('assessment.test.save.keepOne'))).toBeInTheDocument();
  expect(puts()).toEqual([]);
});

test('an attempt submitted elsewhere (no-open-attempt) says so, reads the examination again and links to the result', async () => {
  const user = userEvent.setup();
  answers.answering = async () => refusal(409, 'assessment.no-open-attempt');
  await renderPage();
  answers.reading = async () => Response.json(examination({ attemptsUsed: 1, lastResult: result() }));

  await user.click(within(await questionGroup(1)).getByRole('radio', { name: '30 Sekunden' }));

  expect(await screen.findByText(t('assessment.test.alreadySubmitted'))).toBeInTheDocument();
  expect(await screen.findByRole('link', { name: t('assessment.test.toResult') })).toHaveAttribute(
    'href',
    `/assessment/sessions/${sessionId}/result`,
  );
});

test('nothing about the answers is kept in the browser: the server is the only record', async () => {
  const user = userEvent.setup();
  const setItem = vi.spyOn(Storage.prototype, 'setItem');
  const open = vi.fn();
  vi.stubGlobal('indexedDB', { open });
  onTestFinished(() => {
    setItem.mockRestore();
    vi.unstubAllGlobals();
    vi.stubGlobal('fetch', host);
  });
  await renderPage();

  await user.click(within(await questionGroup(1)).getByRole('radio', { name: '30 Sekunden' }));
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));
  await within(await questionGroup(2)).findByText(t('assessment.test.save.saved'));

  expect(setItem).not.toHaveBeenCalled();
  expect(open).not.toHaveBeenCalled();
});

test('a read on its way when an answer is saved does not take the answer back', async () => {
  notifyAtOnce();
  const user = userEvent.setup();
  const { queryClient } = await renderPage();
  const duration = await questionGroup(1);
  const examinationKey = getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId);
  const reads = () => requestsOf(host.mock.calls).filter(({ method, path }) => method === 'GET' && path === readPath);
  const older = deferred();
  const asked = reads().length;
  answers.reading = () => older.answer;
  const readAgain = queryClient.invalidateQueries({ queryKey: examinationKey });
  await waitFor(() => expect(reads().length).toBeGreaterThan(asked));

  await user.click(within(duration).getByRole('radio', { name: '30 Sekunden' }));
  expect(await within(duration).findByText(t('assessment.test.save.saved'))).toBeInTheDocument();
  older.release(Response.json(inAttempt()));
  await readAgain;

  expect(within(duration).getByRole('radio', { name: '30 Sekunden' })).toBeChecked();
  expect(screen.getByText(t('assessment.test.answered', { answered: 1, total: 2 }))).toBeInTheDocument();
});

test("each question's fieldset is described by its type's hint and its save status", async () => {
  const user = userEvent.setup();
  await renderPage();
  const duration = await questionGroup(1);
  expect(duration).toHaveAccessibleDescription(t('assessment.test.singleHint'));
  expect(await questionGroup(2)).toHaveAccessibleDescription(t('assessment.test.multipleHint'));

  await user.click(within(duration).getByRole('radio', { name: '30 Sekunden' }));
  await within(duration).findByText(t('assessment.test.save.saved'));

  expect(duration).toHaveAccessibleDescription(`${t('assessment.test.singleHint')} ${t('assessment.test.save.saved')}`);
});
