import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { RouterView } from 'vue-router';
import {
  getGetAssessmentExaminationsBySessionSessionIdQueryKey,
  getGetLearningSessionsIdQueryKey,
} from '@/api/generated';
import { t } from '@/i18n';
import {
  chosen,
  deferred,
  durationQuestion,
  examination,
  inAttempt,
  protectionQuestion,
  refusal,
  result,
  saved,
  sessionId,
  thirtySeconds,
  coat,
  unknownRefusal,
} from './examinationFixtures';
import {
  type ExaminationHost,
  type Fetch,
  hostAnswering,
  readPath,
  renderAt,
  requestsOf,
  resultAddress,
  routed,
  submitPath,
  testAddress,
} from './examinationHost';
import ExaminationPage from './ExaminationPage.vue';

// Submitting the test (#48): "Test abgeben" opens ConfirmSubmitDialog, which says how many questions are answered and,
// with some unanswered, that those count as wrong; its initial focus is on "Weiter bearbeiten". "Abgeben" waits until
// every answer on its way is saved (a failed one stops it), posts the submit once, invalidates the examination's and
// the learning session's generated keys, and goes to the result. A 409 no-open-attempt goes there too; any other
// failure is an alert with Retry, in words chosen by the code.
const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let answers: ExaminationHost;
/** One question answered, one not, as the server holds them. */
const halfAnswered = () => inAttempt([chosen(durationQuestion, [thirtySeconds]), protectionQuestion]);

beforeEach(() => {
  answers = hostAnswering(halfAnswered());
  host.mockImplementation((input, init) => routed(answers)(input, init));
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const submits = () =>
  requestsOf(host.mock.calls).filter(({ method, path }) => method === 'POST' && path === submitPath);
const reads = () => requestsOf(host.mock.calls).filter(({ method, path }) => method === 'GET' && path === readPath);
const renderPage = () => renderAt(ExaminationPage, testAddress);

const submitButton = () => screen.findByRole('button', { name: t('assessment.test.submit') });
const dialog = () => screen.findByRole('dialog', { name: t('assessment.test.confirm.title') });
const confirmButton = async () =>
  within(await dialog()).getByRole('button', { name: t('assessment.test.confirm.confirm') });
const cancelButton = async () =>
  within(await dialog()).getByRole('button', { name: t('assessment.test.confirm.cancel') });

/** The question's fieldset, by its legend's "Frage n von 2". */
async function questionGroup(number: number) {
  const label = t('assessment.test.question', { number, total: 2 });
  const groups = await screen.findAllByRole('group');
  const group = groups.find((candidate) => candidate.querySelector('legend')?.textContent?.includes(label) === true);
  if (group === undefined) {
    throw new Error(`No fieldset is headed ${label}.`);
  }
  return group;
}

test('the dialog says how many questions are answered and that unanswered ones count as wrong; the focus is on Weiter bearbeiten', async () => {
  const user = userEvent.setup();
  await renderPage();

  await user.click(await submitButton());

  const asked = await dialog();
  expect(asked).toHaveTextContent(t('assessment.test.confirm.text', { answered: 1, total: 2 }));
  expect(asked).toHaveTextContent(t('assessment.test.confirm.unanswered'));
  const cancel = await cancelButton();
  await waitFor(() => expect(cancel).toHaveFocus());
});

test('with every question answered the dialog gives no warning about unanswered questions', async () => {
  const user = userEvent.setup();
  answers = hostAnswering(inAttempt([chosen(durationQuestion, [thirtySeconds]), chosen(protectionQuestion, [coat])]));
  await renderPage();

  await user.click(await submitButton());

  const asked = await dialog();
  expect(asked).toHaveTextContent(t('assessment.test.confirm.text', { answered: 2, total: 2 }));
  expect(asked).not.toHaveTextContent(t('assessment.test.confirm.unanswered'));
});

test('an answer given just before counts in the dialog', async () => {
  const user = userEvent.setup();
  await renderPage();
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));

  await user.click(await submitButton());

  expect(await dialog()).toHaveTextContent(t('assessment.test.confirm.text', { answered: 2, total: 2 }));
});

test('Weiter bearbeiten closes the dialog, submits nothing, and gives the focus back to Test abgeben', async () => {
  const user = userEvent.setup();
  await renderPage();
  const trigger = await submitButton();
  await user.click(trigger);

  await user.click(await cancelButton());

  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await waitFor(() => expect(trigger).toHaveFocus());
  expect(submits()).toEqual([]);
});

test('Escape closes the dialog and gives the focus back to Test abgeben', async () => {
  const user = userEvent.setup();
  await renderPage();
  const trigger = await submitButton();
  await user.click(trigger);
  await dialog();

  await user.keyboard('{Escape}');

  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await waitFor(() => expect(trigger).toHaveFocus());
  expect(submits()).toEqual([]);
});

test('Abgeben posts the submit once, invalidates the examination and the learning session, and goes to the result', async () => {
  const user = userEvent.setup();
  const { router, queryClient } = await renderPage();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  await user.click(await submitButton());

  await user.click(await confirmButton());

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(submits()).toEqual([{ method: 'POST', path: submitPath, body: undefined }]);
  expect(invalidate).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId) }),
  );
  expect(invalidate).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: getGetLearningSessionsIdQueryKey(sessionId) }),
  );
});

test('Abgeben waits until the answer on its way is saved, and only then submits', async () => {
  const user = userEvent.setup();
  const answer = deferred();
  const log: string[] = [];
  answers.answering = () => {
    log.push('answer sent');
    return answer.answer;
  };
  const submitting = answers.submitting;
  answers.submitting = () => {
    log.push('submit sent');
    return submitting();
  };
  const { router } = await renderPage();
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));
  await user.click(await submitButton());
  await user.click(await confirmButton());

  log.push('answer saved');
  answer.release(saved());

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(log).toEqual(['answer sent', 'answer saved', 'submit sent']);
});

test('an answer that could not be saved stops the submit: the dialog closes and an alert says so', async () => {
  const user = userEvent.setup();
  answers.answering = async () => new Response('', { status: 503 });
  const { router } = await renderPage();
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));
  await within(await questionGroup(2)).findByRole('alert');
  await user.click(await submitButton());

  await user.click(await confirmButton());

  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await screen.findByText(t('assessment.test.unsaved'))).toBeInTheDocument();
  expect(screen.getByText(t('assessment.test.unsaved')).closest('[role="alert"]')).not.toBeNull();
  expect(submits()).toEqual([]);
  expect(router.currentRoute.value.fullPath).toBe(testAddress);
});

test('a double click on Abgeben sends one submit', async () => {
  const user = userEvent.setup();
  const answer = deferred();
  answers.submitting = () => answer.answer;
  const { router } = await renderPage();
  await user.click(await submitButton());
  const confirm = await confirmButton();

  await user.dblClick(confirm);
  await waitFor(() => expect(submits()).toHaveLength(1));
  expect(confirm).toBeDisabled();
  answer.release(Response.json(result()));

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(submits()).toHaveLength(1);
});

test('a submit refused as no-open-attempt (submitted elsewhere) goes to the result as well, and invalidates both keys', async () => {
  const user = userEvent.setup();
  answers.submitting = async () => refusal(409, 'assessment.no-open-attempt');
  const { router, queryClient } = await renderPage();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  answers.reading = async () => Response.json(examination({ attemptsUsed: 1, lastResult: result() }));
  await user.click(await submitButton());

  await user.click(await confirmButton());

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(invalidate).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId) }),
  );
  expect(invalidate).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: getGetLearningSessionsIdQueryKey(sessionId) }),
  );
});

test.each([
  ['examination-changed', 'assessment.test.changed', () => refusal(409, 'assessment.examination-changed')],
  ['identity-incomplete', 'assessment.test.noIdentity', () => refusal(403, 'assessment.identity-incomplete')],
  ['a 500', 'assessment.test.submitFailed', () => new Response('', { status: 500 })],
  ['a network failure', 'assessment.test.submitFailed', () => Promise.reject(new TypeError('Failed to fetch'))],
])('a submit refused with %s says so in an alert, and Retry submits again', async (_case, key, refuse) => {
  const user = userEvent.setup();
  const submitting = answers.submitting;
  answers.submitting = async () => refuse();
  const { router } = await renderPage();
  await user.click(await submitButton());

  await user.click(await confirmButton());

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t(key));
  expect(alert).not.toHaveTextContent('Not for the user.');
  expect(router.currentRoute.value.fullPath).toBe(testAddress);

  answers.submitting = submitting;
  await user.click(within(alert).getByRole('button', { name: t('shell.retry') }));

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(submits()).toHaveLength(2);
});

test('a submit refused with a module code the page does not know says what the host said', async () => {
  const user = userEvent.setup();
  answers.submitting = async () => unknownRefusal(409, 'Der Test ist gerade gesperrt.');
  await renderPage();
  await user.click(await submitButton());

  await user.click(await confirmButton());

  expect(await screen.findByRole('alert')).toHaveTextContent('Der Test ist gerade gesperrt.');
});

test('an answer that failed to save stays failed after its last option is unchecked, and the submit is not sent', async () => {
  const user = userEvent.setup();
  answers.answering = async () => new Response('', { status: 503 });
  const { router } = await renderPage();
  const protection = await questionGroup(2);
  await user.click(within(protection).getByRole('checkbox', { name: 'Kittel' }));
  await within(protection).findByRole('alert');
  await user.click(within(protection).getByRole('checkbox', { name: 'Kittel' }));

  await user.click(await submitButton());
  await user.click(await confirmButton());

  expect(await screen.findByText(t('assessment.test.unsaved'))).toBeInTheDocument();
  const failure = within(await questionGroup(2)).getByRole('alert');
  expect(within(failure).getByRole('button', { name: t('assessment.test.save.retry') })).toBeInTheDocument();
  expect(submits()).toEqual([]);
  expect(router.currentRoute.value.fullPath).toBe(testAddress);
});

test('an answer refused as no-open-attempt while the submit waits for it goes to the result, without a submit', async () => {
  const user = userEvent.setup();
  const answer = deferred();
  answers.answering = () => answer.answer;
  const { router, queryClient } = await renderPage();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  await user.click(within(await questionGroup(2)).getByRole('checkbox', { name: 'Kittel' }));
  await user.click(await submitButton());
  await user.click(await confirmButton());
  answers.reading = async () => Response.json(examination({ attemptsUsed: 1, lastResult: result() }));

  answer.release(refusal(409, 'assessment.no-open-attempt'));

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(resultAddress));
  expect(submits()).toEqual([]);
  expect(screen.queryByText(t('assessment.test.unsaved'))).toBeNull();
  expect(invalidate).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: getGetLearningSessionsIdQueryKey(sessionId) }),
  );
});

describe('the result after the submit', () => {
  /** The app as the router shows it: whichever page the address names, the test page or the result page. */
  const Shell = defineComponent({ name: 'ExaminationShell', render: () => h(RouterView) });

  /** Every text the container showed, as it changed. */
  function textsShown(container: Element) {
    const texts = [container.textContent ?? ''];
    const observer = new MutationObserver(() => texts.push(container.textContent ?? ''));
    observer.observe(container, { subtree: true, childList: true, characterData: true });
    return { texts, stop: () => observer.disconnect() };
  }

  test.each([
    ['the first attempt, with no result before it', null, 1],
    ['the second attempt, after a failed first', result({ number: 1 }), 2],
  ] as const)(
    'after %s the result page never shows what was there before the submit',
    async (_case, before, number) => {
      const user = userEvent.setup();
      answers = hostAnswering(
        inAttempt([chosen(durationQuestion, [thirtySeconds]), chosen(protectionQuestion, [coat])], {
          attemptsUsed: number - 1,
          lastResult: before,
        }),
      );
      const passed = result({ number, outcome: 'Passed', scorePercent: 100, chaptersToRepeat: [], attemptsLeft: 0 });
      answers.submitting = async () => Response.json(passed);
      const { router, container } = await renderAt(Shell, testAddress);
      await user.click(await submitButton());
      const reread = deferred();
      const asked = reads().length;
      // Each read gets a copy: a page that reads twice must not find the answer's body used.
      answers.reading = () => reread.answer.then((response) => response.clone());
      const shown = textsShown(container);

      await user.click(await confirmButton());
      await waitFor(() => expect(reads().length).toBeGreaterThan(asked));
      reread.release(
        Response.json(
          examination({ status: 'Passed', attemptsUsed: number, currentAttempt: null, lastResult: passed }),
        ),
      );

      expect(await screen.findByText(t('assessment.result.attempt', { number }))).toBeInTheDocument();
      expect(screen.getByText(t('assessment.result.outcome.Passed'))).toBeInTheDocument();
      expect(router.currentRoute.value.fullPath).toBe(resultAddress);
      shown.stop();
      for (const text of shown.texts) {
        expect(text).not.toContain(t('assessment.result.none'));
        expect(text).not.toContain(t('assessment.result.outcome.Failed'));
        expect(text).not.toContain(t('assessment.result.attempt', { number: number - 1 }));
      }
    },
  );
});
