import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { t } from '@/i18n';
import {
  anna,
  ben,
  carla,
  catalogPath,
  chooseButton,
  david,
  dueDateField,
  type Fetch,
  hostFrom,
  hostState,
  type HostState,
  hygiene,
  markt,
  now,
  postsOf,
  problem,
  readsOf,
  renderAssignPage,
  stepHeading,
  submitButton,
  toReview,
} from './assignCourseFixtures';

// The wizard's last step (#37, T4): the assignment is sent once, however often the button is pressed, and the result
// names who was assigned and who was not, by name and in the UI's own words for the refusals it knows. A refusal of
// the whole request lands where the user can act on it.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let state: HostState;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  state = hostState();
  host.mockImplementation(hostFrom(state));
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

const assignmentsUrl = `/api/compliance/pharmacies/${markt.id}/assignments`;
const assigned = (count: number) => t('compliance.assign.result.assigned', { count }, count);

describe('submitting', () => {
  test('sends the course, the people, the due date and a yearly recurrence as 12 months', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user, { members: [anna, ben], due: '2027-03-15', yearly: true });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    expect(postsOf(host)).toEqual([
      {
        url: assignmentsUrl,
        body: {
          courseId: hygiene.courseId,
          personIds: [anna.personId, ben.personId],
          dueDate: '2027-03-15',
          recurrenceMonths: 12,
        },
      },
    ]);
  });

  test('sends no recurrence as null', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user, { members: [anna], yearly: false });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    expect(postsOf(host).map(({ body }) => body)).toEqual([
      { courseId: hygiene.courseId, personIds: [anna.personId], dueDate: '2027-03-15', recurrenceMonths: null },
    ]);
  });

  test('sends once on a double click, the button disabled while it is sent', async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    state.assignment = () => new Promise<Response>((resolve) => (answer = resolve));
    await renderAssignPage();
    await toReview(user);

    await user.dblClick(submitButton());

    await waitFor(() => expect(submitButton()).toBeDisabled());
    expect(postsOf(host)).toHaveLength(1);
    answer(Response.json({ assigned: [], refused: [] }));
    await stepHeading('compliance.assign.result.title');
    expect(postsOf(host)).toHaveLength(1);
  });
});

describe('the result', () => {
  test('takes the focus, says how many were assigned and reports it in a toast', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user, { members: [anna, ben] });

    await user.click(submitButton());

    const heading = await stepHeading('compliance.assign.result.title');
    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByText(assigned(2))).toBeInTheDocument();
    expect(notify.success).toHaveBeenCalledWith(assigned(2));
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('names each person refused, in its own words for the refusals it knows and the host’s for others', async () => {
    const user = userEvent.setup();
    state.assignment = async () =>
      Response.json({
        assigned: [{ personId: anna.personId, assignmentId: '0198c1a0-0000-7000-8000-0000000000f1' }],
        refused: [
          {
            personId: ben.personId,
            code: 'compliance.assignment-already-active',
            message: `Für ${ben.personId} ist die Schulung bereits aktiv zugewiesen.`,
          },
          {
            personId: carla.personId,
            code: 'compliance.learner-not-a-member',
            message: `${carla.personId} ist kein Mitglied der Apotheke.`,
          },
          { personId: david.personId, code: 'compliance.something-new', message: 'Etwas Neues ist geschehen.' },
        ],
      });
    await renderAssignPage();
    await toReview(user, { members: [anna, ben, carla, david] });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    expect(screen.getByText(assigned(1))).toBeInTheDocument();
    const refused = screen.getByRole('list', { name: t('compliance.assign.result.refused') });
    expect(
      within(refused)
        .getAllByRole('listitem')
        .map((item) => item.textContent.trim()),
    ).toEqual([
      t('compliance.assign.result.alreadyActive', { name: ben.name }),
      t('compliance.assign.result.notAMember', { name: carla.name }),
      t('compliance.assign.result.other', { name: david.name, message: 'Etwas Neues ist geschehen.' }),
    ]);
    expect(refused).not.toHaveTextContent(ben.personId);
    expect(refused).not.toHaveTextContent(carla.personId);
    expect(notify.success).toHaveBeenCalledWith(assigned(1));
  });

  test('with nobody assigned reports a failure instead of a success', async () => {
    const user = userEvent.setup();
    state.assignment = async () =>
      Response.json({
        assigned: [],
        refused: [{ personId: anna.personId, code: 'compliance.assignment-already-active', message: 'bereits aktiv' }],
      });
    await renderAssignPage();
    await toReview(user, { members: [anna] });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    expect(notify.failure).toHaveBeenCalledWith(t('compliance.assign.result.noneAssigned'));
    expect(notify.success).not.toHaveBeenCalled();
    expect(
      within(screen.getByRole('list', { name: t('compliance.assign.result.refused') })).getByText(
        t('compliance.assign.result.alreadyActive', { name: anna.name }),
      ),
    ).toBeInTheDocument();
  });

  test('leads home, or to another assignment that starts afresh on the course step', async () => {
    const user = userEvent.setup();
    await renderAssignPage();
    await toReview(user);
    await user.click(submitButton());
    await stepHeading('compliance.assign.result.title');

    expect(screen.getByRole('link', { name: t('compliance.assign.result.home') })).toHaveAttribute('href', '/');
    await user.click(screen.getByRole('button', { name: t('compliance.assign.result.again') }));

    const heading = await stepHeading('compliance.assign.course.title');
    await waitFor(() => expect(heading).toHaveFocus());
    expect(await chooseButton(hygiene)).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('a refusal of the whole request', () => {
  test.each([
    [422, 'compliance.due-date-in-the-past', 'compliance.assign.due.inThePast'],
    [400, 'compliance.due-date-too-far', 'compliance.assign.due.tooFar'],
    [400, 'compliance.due-date-missing', 'compliance.assign.due.required'],
  ])('%i %s goes back to the due date with the error at the field', async (status, code, message) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, 'Das Fälligkeitsdatum ist ungültig.');
    await renderAssignPage();
    await toReview(user);

    await user.click(submitButton());

    await stepHeading('compliance.assign.due.title');
    expect(await screen.findByRole('alert')).toHaveTextContent(t(message));
    expect(dueDateField()).toHaveAttribute('aria-invalid', 'true');
  });

  test.each([
    [409, 'compliance.course-retired'],
    [404, 'compliance.course-not-found'],
  ])('%i %s goes back to the course step with an alert, and reads the catalog again', async (status, code) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, 'Die Schulung ist nicht mehr verfügbar.');
    await renderAssignPage();
    await toReview(user);
    const catalogReads = readsOf(host, catalogPath).length;

    await user.click(submitButton());

    await stepHeading('compliance.assign.course.title');
    expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.course.gone'));
    await waitFor(() => expect(readsOf(host, catalogPath).length).toBeGreaterThan(catalogReads));
  });

  test.each([
    [403, 'compliance.capability-missing', 'Sie dürfen in dieser Apotheke keine Schulungen zuweisen.'],
    [404, 'compliance.pharmacy-not-found', 'Die Apotheke wurde nicht gefunden.'],
  ])("%i %s shows the host's message instead of the form", async (status, code, message) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, message);
    await renderAssignPage();
    await toReview(user);

    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByRole('list', { name: t('compliance.assign.steps.label') })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('compliance.assign.submit') })).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 1, name: t('modules.compliance.pages.assign.title') }),
    ).toBeInTheDocument();
  });

  test.each([
    [409, 'compliance.assignments-changed'],
    [409, 'already-exists'],
  ])('%i %s says so on the review, which keeps the selection to send again', async (status, code) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, 'Die Zuweisungen haben sich geändert.');
    await renderAssignPage();
    await toReview(user, { members: [anna, ben] });

    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.changed'));
    expect(screen.getByRole('heading', { level: 2, name: t('compliance.assign.review.title') })).toBeInTheDocument();
    expect(screen.getByText(anna.name, { exact: false })).toBeInTheDocument();
    expect(screen.getByText(ben.name, { exact: false })).toBeInTheDocument();
    await waitFor(() => expect(submitButton()).toBeEnabled());

    state.assignment = hostState().assignment;
    await user.click(submitButton());
    await stepHeading('compliance.assign.result.title');
    expect(postsOf(host)).toHaveLength(2);
    expect(postsOf(host)[1]?.body).toEqual(postsOf(host)[0]?.body);
  });

  test.each([
    ['a failure of the host', () => new Response('', { status: 500 })],
    ['a request too large', () => new Response('', { status: 413 })],
    ['too many learners', () => problem(400, 'compliance.too-many-learners', 'Zu viele Personen.')],
    ['an invalid recurrence', () => problem(400, 'compliance.invalid-recurrence', 'Ungültige Wiederholung.')],
    ['no learners', () => problem(400, 'compliance.no-learners', 'Keine Personen.')],
  ])('%s shows a failure on the review', async (_name, refusal) => {
    const user = userEvent.setup();
    state.assignment = async () => refusal();
    await renderAssignPage();
    await toReview(user);

    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.failed'));
    expect(screen.getByRole('heading', { level: 2, name: t('compliance.assign.review.title') })).toBeInTheDocument();
    await waitFor(() => expect(submitButton()).toBeEnabled());
  });
});
