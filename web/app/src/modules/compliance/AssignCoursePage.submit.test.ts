import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import {
  anna,
  ben,
  assignedAll,
  carla,
  catalogPath,
  chooseButton,
  david,
  dom,
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

  // A refused learner (#147): the page's words for the two it knows, the catalog's for another code it has, the host's
  // only for a code the catalog does not know, and "Nicht zugewiesen." when there are none.
  test('a person refused with a code the catalog knows is named with its catalog text, never the host words', async () => {
    const user = userEvent.setup();
    state.assignment = async () =>
      Response.json({
        assigned: [],
        refused: [
          { personId: ben.personId, code: 'compliance.invalid-person', message: 'SERVER WORDS' },
          { personId: carla.personId, code: 'compliance.something-new', message: '' },
        ],
      });
    await renderAssignPage();
    await toReview(user, { members: [anna, ben, carla] });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    const refused = screen.getByRole('list', { name: t('compliance.assign.result.refused') });
    expect(
      within(refused)
        .getAllByRole('listitem')
        .map((item) => item.textContent.trim()),
    ).toEqual([
      t('compliance.assign.result.other', {
        name: ben.name,
        message: 'Eine der gewählten Personen ist ungültig. Laden Sie die Seite neu.',
      }),
      t('compliance.assign.result.other', { name: carla.name, message: 'Nicht zugewiesen.' }),
    ]);
    expect(refused).not.toHaveTextContent('SERVER WORDS');
  });

  test('a person refused with a code the catalog knows is named with its catalog text in English', async () => {
    setLanguage('en-GB', { remember: false });
    const user = userEvent.setup();
    state.assignment = async () =>
      Response.json({
        assigned: [],
        refused: [{ personId: ben.personId, code: 'compliance.invalid-person', message: 'SERVER WORDS' }],
      });
    await renderAssignPage();
    await toReview(user, { members: [anna, ben] });

    await user.click(submitButton());

    await stepHeading('compliance.assign.result.title');
    const refused = screen.getByRole('list', { name: t('compliance.assign.result.refused') });
    expect(refused).toHaveTextContent(`${ben.name}: One of the chosen people is not valid. Reload the page.`);
    expect(refused).not.toHaveTextContent('SERVER WORDS');
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

// Follow-ups of #37 (#239): an answer that comes after the user switched the pharmacy is the old pharmacy's.
describe('switching the pharmacy while the assignment is on its way', () => {
  test("drops the old pharmacy's answer: the new pharmacy's wizard stays on the course step, without a result or a toast", async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    state.memberships = [markt, dom];
    state.assignment = () => new Promise<Response>((resolve) => (answer = resolve));
    localStorage.setItem('pharmacy', markt.id);
    const { pinia, queryClient } = await renderAssignPage();
    await toReview(user, { members: [anna, ben] });
    await user.click(submitButton());
    await waitFor(() => expect(postsOf(host)).toHaveLength(1));

    useSelectedPharmacyStore(pinia).choose(dom.id);
    await stepHeading('compliance.assign.course.title');
    answer(
      Response.json({
        assigned: [{ personId: anna.personId, assignmentId: '0198c1a0-0000-7000-8000-0000000000f1' }],
        refused: [{ personId: ben.personId, code: 'compliance.assignment-already-active', message: 'bereits aktiv' }],
      }),
    );
    await waitFor(() => expect(queryClient.isMutating()).toBe(0));

    await expect(stepHeading('compliance.assign.result.title')).rejects.toThrow();
    expect(screen.getByRole('heading', { level: 2, name: t('compliance.assign.course.title') })).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(ben.personId);
    expect(notify.success).not.toHaveBeenCalled();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test("the new pharmacy's wizard can be sent while the old pharmacy's answer is still on its way", async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    state.memberships = [markt, dom];
    state.members[dom.id] = [carla];
    let first = true;
    state.assignment = (body) => {
      if (first) {
        first = false;
        return new Promise<Response>((resolve) => (answer = resolve));
      }
      return assignedAll(body);
    };
    localStorage.setItem('pharmacy', markt.id);
    const { pinia } = await renderAssignPage();
    await toReview(user, { members: [anna, ben] });
    await user.click(submitButton());
    await waitFor(() => expect(postsOf(host)).toHaveLength(1));

    useSelectedPharmacyStore(pinia).choose(dom.id);
    await stepHeading('compliance.assign.course.title');
    await toReview(user, { members: [carla] });

    expect(submitButton()).toBeEnabled();
    await user.click(submitButton());
    await stepHeading('compliance.assign.result.title');
    expect(postsOf(host)).toHaveLength(2);
    answer(Response.json({ assigned: [], refused: [] }));
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

  // The page's own words for capability-missing, the catalog's for pharmacy-not-found (#147), never the host's.
  test.each([
    [403, 'compliance.capability-missing', 'Sie dürfen in dieser Apotheke keine Schulungen zuweisen.'],
    [404, 'compliance.pharmacy-not-found', 'Sie sind nicht mehr Mitglied dieser Apotheke.'],
  ])('%i %s shows its text instead of the form, never the host words', async (status, code, message) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, 'SERVER WORDS');
    await renderAssignPage();
    await toReview(user);

    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.getByRole('alert')).not.toHaveTextContent('SERVER WORDS');
    expect(screen.queryByRole('list', { name: t('compliance.assign.steps.label') })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('compliance.assign.submit') })).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 1, name: t('modules.compliance.pages.assign.title') }),
    ).toBeInTheDocument();
  });

  // Follow-ups of #37 (#239): the section that held the focused Submit button is gone, so the alert takes the focus.
  test.each([
    [403, 'compliance.capability-missing', 'Sie dürfen in dieser Apotheke keine Schulungen zuweisen.'],
    [404, 'compliance.pharmacy-not-found', 'Sie sind nicht mehr Mitglied dieser Apotheke.'],
  ])('%i %s gives its message the focus', async (status, code, message) => {
    const user = userEvent.setup();
    state.assignment = async () => problem(status, code, 'SERVER WORDS');
    await renderAssignPage();
    await toReview(user);

    await user.click(submitButton());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(message);
    expect(alert).toHaveAttribute('tabindex', '-1');
    await waitFor(() => expect(alert).toHaveFocus());
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
