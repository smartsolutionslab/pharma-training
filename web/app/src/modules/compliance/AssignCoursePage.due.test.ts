import { cleanup, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  anna,
  backButton,
  ben,
  dueDateField,
  type Fetch,
  hostFrom,
  hostState,
  hygiene,
  nextButton,
  now,
  postsOf,
  renderAssignPage,
  repeatYearly,
  setDueDate,
  stepHeading,
  submitButton,
  today,
  toDueDate,
  toReview,
} from './assignCourseFixtures';

// The wizard's third and fourth steps (#37, T3): the due date, today or later and at most ten years ahead, with an
// optional yearly recurrence; then the review of everything before it is sent.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  host.mockImplementation(hostFrom(hostState()));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

describe('the due date step', () => {
  test('asks for the due date in a date field that starts today, and offers a yearly recurrence, off', async () => {
    const user = userEvent.setup();
    await renderAssignPage();

    await toDueDate(user);

    expect(dueDateField()).toHaveAttribute('type', 'date');
    expect(dueDateField()).toHaveAttribute('min', today);
    expect(dueDateField()).toHaveValue('');
    expect(repeatYearly()).not.toBeChecked();
  });

  test('Next without a due date is refused at the field, and the alert takes the focus', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toDueDate(user);

    await user.click(nextButton());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t('compliance.assign.due.required'));
    expect(dueDateField()).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => expect(alert).toHaveFocus());
    expect(screen.queryByRole('heading', { name: t('compliance.assign.review.title') })).not.toBeInTheDocument();
  });

  test('a due date before today is refused at the field', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toDueDate(user);

    await setDueDate('2026-11-22');
    await user.click(nextButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.due.inThePast'));
    expect(dueDateField()).toHaveAttribute('aria-invalid', 'true');
  });

  test('a due date more than ten years ahead is refused at the field', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toDueDate(user);

    await setDueDate('2036-11-24');
    await user.click(nextButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.due.tooFar'));
  });

  test('today is a due date the step takes', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toDueDate(user);

    await setDueDate(today);
    await user.click(nextButton());

    expect(await stepHeading('compliance.assign.review.title')).toBeInTheDocument();
  });
});

// Follow-ups of #37 (#239): the clock moves on while the wizard is open.
describe('the due date as the clock moves on', () => {
  test("the date field's earliest day follows the clock, read again when the step shows again", async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toDueDate(user);
    expect(dueDateField()).toHaveAttribute('min', today);

    await user.click(backButton());
    await stepHeading('compliance.assign.learners.title');
    // Past midnight: the next day, in Germany and in the browser's zone alike.
    vi.setSystemTime(new Date('2026-11-24T11:00:00Z'));
    await user.click(nextButton());
    await stepHeading('compliance.assign.due.title');

    expect(dueDateField()).toHaveAttribute('min', '2026-11-24');
  });

  test('the date field ends ten years from today, as the host takes it', async () => {
    const user = userEvent.setup();
    await renderAssignPage();

    await toDueDate(user);

    expect(dueDateField()).toHaveAttribute('max', '2036-11-23');
  });

  test('a review left open past midnight checks the due date again at submit: nothing is sent, and the step says so', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user, { due: today });

    // Past midnight: the due date chosen as today is now yesterday.
    vi.setSystemTime(new Date('2026-11-24T11:00:00Z'));
    await user.click(submitButton());

    await stepHeading('compliance.assign.due.title');
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t('compliance.assign.due.inThePast'));
    expect(dueDateField()).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => expect(alert).toHaveFocus());
    expect(postsOf(host)).toEqual([]);
    expect(notify.success).not.toHaveBeenCalled();
  });
});

describe('the review', () => {
  /** The value the review gives a term of its description list. */
  function described(term: string) {
    const terms = screen.getAllByRole('term');
    const found = terms.find((each) => each.textContent.trim() === term);
    expect(found, `the review has the term ${term}`).toBeDefined();
    const value = found?.nextElementSibling;
    expect(value?.tagName).toBe('DD');
    return value as HTMLElement;
  }

  test('describes the course, the learners by name, the due date in full and the yearly recurrence', async () => {
    const user = userEvent.setup();
    await renderAssignPage();

    await toReview(user, { members: [anna, ben], due: '2027-03-15', yearly: true });

    expect(described(t('compliance.assign.review.course'))).toHaveTextContent(hygiene.title);
    const learners = described(t('compliance.assign.review.learners', { count: 2 }));
    expect(learners).toHaveTextContent(anna.name);
    expect(learners).toHaveTextContent(ben.name);
    expect(described(t('compliance.assign.review.due'))).toHaveTextContent('15. März 2027');
    expect(described(t('compliance.assign.review.recurrence'))).toHaveTextContent(t('compliance.assign.review.yearly'));
    expect(screen.getByRole('button', { name: t('compliance.assign.submit') })).toBeEnabled();
  });

  test('says when the assignment is not repeated', async () => {
    const user = userEvent.setup();
    await renderAssignPage();

    await toReview(user, { yearly: false });

    expect(described(t('compliance.assign.review.recurrence'))).toHaveTextContent(t('compliance.assign.review.once'));
  });

  test('gives the due date in English in full, too', async () => {
    const user = userEvent.setup();
    setLanguage('en-GB', { remember: false });
    await renderAssignPage();

    await toReview(user, { due: '2027-03-15' });

    expect(described(t('compliance.assign.review.due'))).toHaveTextContent('15 March 2027');
  });

  test('Back from the review keeps the due date and the recurrence', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user, { due: '2027-03-15', yearly: true });

    await user.click(screen.getByRole('button', { name: t('compliance.assign.back') }));

    await stepHeading('compliance.assign.due.title');
    expect(dueDateField()).toHaveValue('2027-03-15');
    expect(repeatYearly()).toBeChecked();
  });
});
