import { QueryClient } from '@tanstack/vue-query';
import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, onTestFinished, test, vi } from 'vitest';
import {
  type EditorialCourseVersionDto,
  type EditorialCourseVersionStatus,
  type EditorialQuestionDto,
  getGetEditorialCourseVersionsIdQueryKey,
} from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { handWritten, questionsUpTo, versionId, versionOf } from './courseTestFixtures';
import {
  answering,
  dialogClosings,
  type Fetch,
  refused,
  refusedWith,
  renderVersionPage,
  requestsOf,
  stepTaken,
  tryToClose,
  type WorkflowHost,
  workflowHost,
} from './workflowPageHost';

// The course version page's workflow (#26): a section "Prüfung und Veröffentlichung" between the header and the
// chapters, with the publishing checklist for a draft and a version in review, and the step the version's status
// allows: Submit a draft (no confirmation: Return undoes it), Publish (confirmed) or Return a version in review, Retire a
// published or superseded one (confirmed, destructive). The buttons stay enabled whatever the checklist says: the host
// decides. Only the toasts announce; afterwards the focus is on the heading once the version shows its new status, or
// after a refusal on the button pressed while it is still there. A publish four eyes refused adds why to the checklist.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: WorkflowHost;

beforeEach(() => {
  host = workflowHost();
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const title = 'Hygiene in der Apotheke';

/** As many approved questions as the blueprint asks for (10): a version that may be published. */
const complete = (): EditorialQuestionDto[] =>
  questionsUpTo(10).map((question) => ({ ...question, review: 'Approved' }));

function showing(version: EditorialCourseVersionDto) {
  host.version = version;
}

const renderPage = () => renderVersionPage(versionId, title);

const posts = () => requestsOf(fetch).filter(({ method }) => method === 'POST');
const reads = () => requestsOf(fetch).filter(({ method }) => method === 'GET');

const workflowSection = () => screen.getByRole('region', { name: t('editorial.workflow.title') });
const checklist = () => screen.queryByRole('list', { name: t('editorial.workflow.checklist.title') });
/** The checklist's item whose text holds the text; none without that item or without a checklist. */
function checklistItem(text: string) {
  const list = checklist();
  if (list === null) {
    return null;
  }
  return (
    within(list)
      .queryAllByRole('listitem')
      .find((item) => item.textContent?.includes(text)) ?? null
  );
}

/** The workflow's buttons on the page, by their catalog keys. */
const actions = {
  submit: 'editorial.workflow.submit',
  publish: 'editorial.workflow.publish',
  return: 'editorial.workflow.return',
  retire: 'editorial.workflow.retire',
} as const;
type Action = keyof typeof actions;
const actionButton = (action: Action) => screen.queryByRole('button', { name: t(actions[action]) });

const statusText = (status: EditorialCourseVersionStatus) => t(`glossary.courseVersionStatus.${status}`);

describe('the workflow section', () => {
  test('is headed "Prüfung und Veröffentlichung", between the header and the chapters', async () => {
    const { heading } = await renderPage();

    const section = workflowSection();
    expect(within(section).getByRole('heading', { level: 2, name: 'Prüfung und Veröffentlichung' })).toBeVisible();
    const chapters = screen.getByRole('heading', { level: 2, name: t('editorial.chapters.title') });
    expect(heading.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(section.compareDocumentPosition(chapters) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  // Status, whether the checklist shows, and the steps offered.
  const table: [EditorialCourseVersionStatus, boolean, Action[]][] = [
    ['Draft', true, ['submit']],
    ['InReview', true, ['publish', 'return']],
    ['Published', false, ['retire']],
    ['Superseded', false, ['retire']],
  ];
  // A retired version offers no step: shown once it is retired below (it offers none today either, so a row of its own
  // here would arrive green).

  test.each(table)('of a version %s shows the checklist: %s, and offers %j', async (status, listed, offered) => {
    showing(versionOf(status));
    await renderPage();

    expect(checklist() !== null).toBe(listed);
    if (status === 'Published') {
      // Its next version is still offered beside Retire.
      expect(screen.getByRole('button', { name: t('editorial.version.newVersion') })).toBeEnabled();
    }
    for (const action of Object.keys(actions) as Action[]) {
      if (offered.includes(action)) {
        expect(actionButton(action), action).toBeEnabled();
      } else {
        expect(actionButton(action), action).toBeNull();
      }
    }
  });

  test('names its steps in German', async () => {
    showing(versionOf('InReview'));
    await renderPage();

    expect(screen.getByRole('button', { name: 'Veröffentlichen' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Zur Überarbeitung zurückgeben' })).toBeVisible();
  });

  test.each([
    ['Draft', 'Submit for review'],
    ['InReview', 'Publish'],
    ['InReview', 'Return to draft'],
    ['Published', 'Retire'],
  ] as const)('of a version %s names its step "%s" in English', async (status, name) => {
    setLanguage('en-GB', { remember: false });
    showing(versionOf(status));
    await renderPage();

    expect(screen.getByRole('heading', { level: 2, name: 'Review and publishing' })).toBeVisible();
    expect(screen.getByRole('button', { name })).toBeEnabled();
  });

  test('offers Submit in German', async () => {
    await renderPage();

    expect(screen.getByRole('button', { name: 'Zur Prüfung einreichen' })).toBeEnabled();
  });
});

describe('the checklist', () => {
  test('of a draft says what is met and what is open, each with its icon hidden from screen readers', async () => {
    await renderPage();

    const list = checklist();
    expect(list?.tagName).toBe('UL');
    const items = within(list ?? document.body).getAllByRole('listitem');
    expect(items).toHaveLength(4);
    const expected: [string, string][] = [
      ['Erfüllt:', 'Die Version hat Kapitel.'],
      ['Offen:', 'Der Test hat 3 von 10 verlangten Fragen.'],
      ['Offen:', 'Eine von 3 Fragen ist geprüft.'],
      ['Offen:', 'Die Version muss noch zur Prüfung eingereicht werden.'],
    ];
    expected.forEach(([state, text], index) => {
      const item = items[index] ?? document.body;
      expect(within(item).getByText(state)).toBeInTheDocument();
      expect(item).toHaveTextContent(text);
      const icon = item.querySelector('svg');
      expect(icon).not.toBeNull();
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
  });

  test('of a draft without chapters says it needs one', async () => {
    showing(versionOf('Draft', { chapters: [] }));
    await renderPage();

    expect(checklistItem('Die Version braucht mindestens ein Kapitel.')).not.toBeNull();
  });

  test('of a version in review ready to publish has every item done', async () => {
    showing(versionOf('InReview', { questions: complete() }));
    await renderPage();

    const items = within(checklist() ?? document.body).getAllByRole('listitem');
    expect(items).toHaveLength(4);
    for (const item of items) {
      expect(within(item).getByText(t('editorial.workflow.checklist.done'))).toBeInTheDocument();
    }
    expect(checklistItem('Der Test hat alle 10 verlangten Fragen.')).not.toBeNull();
    expect(checklistItem('Alle Fragen sind geprüft.')).not.toBeNull();
    expect(checklistItem('Die Version ist zur Prüfung eingereicht.')).not.toBeNull();
  });

  test('is not a live region: only the toasts announce', async () => {
    await renderPage();

    expect(checklist()?.closest('[aria-live], [role="status"], [role="alert"], [role="log"]')).toBeNull();
  });

  test('blocks nothing: Submit stays enabled while items are open', async () => {
    showing(versionOf('Draft', { chapters: [], questions: [] }));
    await renderPage();

    expect(actionButton('submit')).toBeEnabled();
  });

  // A blueprint asking for one question: the item reads in the singular.
  const one = { questions: 1, passingScorePercent: 80, validityMonths: 12 };

  test.each([
    [1, 'Erfüllt:', 'Der Test hat die verlangte Frage.'],
    [0, 'Offen:', 'Der Test hat die verlangte Frage noch nicht.'],
    [2, 'Offen:', 'Der Test hat 2 Fragen, verlangt ist eine.'],
  ])('of a test asking for one question and holding %i says so in the singular', async (held, state, text) => {
    showing(versionOf('Draft', { questions: questionsUpTo(held), blueprint: one }));
    await renderPage();

    const item = checklistItem(text);
    expect(item).not.toBeNull();
    expect(within(item ?? document.body).getByText(state)).toBeInTheDocument();
  });

  test.each([
    [1, 'The test has the one question its blueprint asks for.'],
    [0, 'The test does not have the one question its blueprint asks for yet.'],
    [2, 'The test has 2 questions; its blueprint asks for one.'],
  ])('of a test asking for one question and holding %i says so in the singular in English', async (held, text) => {
    setLanguage('en-GB', { remember: false });
    showing(versionOf('Draft', { questions: questionsUpTo(held), blueprint: one }));
    await renderPage();

    expect(checklistItem(text)).not.toBeNull();
  });

  test('of a test with one of its questions approved says one is', async () => {
    showing(versionOf('Draft', { questions: [{ ...handWritten, review: 'Approved' }, ...questionsUpTo(2).slice(1)] }));
    await renderPage();

    expect(checklistItem('Eine von 2 Fragen ist geprüft.')).not.toBeNull();
  });

  test('of a test with every question approved says they are checked, as the test page does', async () => {
    showing(versionOf('InReview', { questions: complete() }));
    await renderPage();

    expect(checklistItem('Alle Fragen sind geprüft.')).not.toBeNull();
  });
});

describe('submitting a draft', () => {
  test('posts at once, reports it, and puts the focus on the heading once the version is in review', async () => {
    const user = userEvent.setup();
    const { heading } = await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByText(statusText('InReview'))).toBeVisible();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(posts()).toEqual([
      { method: 'POST', path: `/editorial/course-versions/${versionId}/submit`, body: undefined },
    ]);
    expect(notify.success).toHaveBeenCalledWith('Version 2 ist zur Prüfung eingereicht.');
    expect(notify.failure).not.toHaveBeenCalled();
    expect(actionButton('publish')).toBeEnabled();
  });

  test('reports it in English', async () => {
    setLanguage('en-GB', { remember: false });
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: 'Submit for review' }));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Version 2 has been submitted for review.'));
  });

  test('disables every step while the host has not answered, and posts once', async () => {
    let answer: (response: Response) => void = () => undefined;
    host.step = () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      });
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(actionButton('submit')).toBeDisabled());
    await user.click(actionButton('submit') ?? document.body);
    expect(posts()).toHaveLength(1);
    answer(stepTaken(host, 'submit'));
    await waitFor(() => expect(notify.success).toHaveBeenCalled());
  });

  test('refused for want of chapters says so in its own words and keeps the focus on Submit', async () => {
    host.step = async () => refused(409, 'editorial.no-chapters');
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Version braucht mindestens ein Kapitel.'));
    await waitFor(() => expect(actionButton('submit')).toHaveFocus());
    expect(actionButton('submit')).toBeEnabled();
    expect(notify.success).not.toHaveBeenCalled();
  });

  test('failing on the network says the version could not be submitted', async () => {
    host.step = () => Promise.reject(new TypeError('Failed to fetch'));
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Die Version konnte nicht eingereicht werden. Versuchen Sie es erneut.',
      ),
    );
  });

  test("refused by a policy without a code says the editor may not, never the host's words", async () => {
    host.step = async () => refused(403);
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Sie dürfen diesen Schritt nicht ausführen.'));
    expect(notify.failure).not.toHaveBeenCalledWith(expect.stringContaining('Words of the host'));
  });
});

describe('publishing a version in review', () => {
  beforeEach(() => {
    showing(versionOf('InReview', { questions: complete() }));
  });

  async function openPublish() {
    const user = userEvent.setup();
    const rendered = await renderPage();
    await user.click(actionButton('publish') ?? document.body);
    const dialog = await screen.findByRole('dialog', { name: 'Version veröffentlichen?' });
    return { user, dialog, ...rendered };
  }

  test('asks first, naming the version and what follows, and posts nothing until confirmed', async () => {
    const { dialog } = await openPublish();

    expect(dialog).toHaveAccessibleDescription(
      'Version 2 von „Hygiene in der Apotheke“ wird veröffentlicht und kann danach nicht mehr geändert werden. Eine bisher veröffentlichte Version dieser Schulung wird abgelöst.',
    );
    expect(within(dialog).getByRole('button', { name: 'Veröffentlichen' })).not.toHaveClass('bg-destructive');
    expect(posts()).toEqual([]);
  });

  test('cancelled, posts nothing', async () => {
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(posts()).toEqual([]);
    expect(notify.success).not.toHaveBeenCalled();
  });

  test('confirmed, posts, reports it and puts the focus on the heading once it is published', async () => {
    const { user, dialog, heading } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByText(statusText('Published'))).toBeVisible();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(posts()).toEqual([
      { method: 'POST', path: `/editorial/course-versions/${versionId}/publish`, body: undefined },
    ]);
    expect(notify.success).toHaveBeenCalledWith('Version 2 ist veröffentlicht.');
  });

  test('disables the confirmation while the host has not answered', async () => {
    host.step = () => new Promise<Response>(() => undefined);
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Veröffentlichen' })).toBeDisabled());
    expect(posts()).toHaveLength(1);
  });

  test('refused by four eyes as the submitter says why, adds it to the checklist and keeps the focus on Publish', async () => {
    host.step = async () => refused(403, 'editorial.four-eyes');
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Mit Vier-Augen-Freigabe darf nicht veröffentlichen, wer die Version eingereicht oder ihre Testvorgaben zuletzt geändert hat. Bitten Sie eine andere Person aus der Redaktion.',
      ),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(actionButton('publish')).toHaveFocus());
    const item = checklistItem(
      'Sie haben diese Version eingereicht oder ihre Testvorgaben zuletzt geändert. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.',
    );
    expect(item).not.toBeNull();
    expect(within(item ?? document.body).getByText(t('editorial.workflow.checklist.open'))).toBeInTheDocument();
    expect(within(checklist() ?? document.body).getAllByRole('listitem')).toHaveLength(5);
  });

  test('refused by four eyes as an approver of its questions says why and adds that to the checklist', async () => {
    host.step = async () => refused(403, 'editorial.publisher-approved-questions');
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Mit Vier-Augen-Freigabe darf nicht veröffentlichen, wer Fragen dieser Version freigegeben hat. Bitten Sie eine andere Person aus der Redaktion.',
      ),
    );
    await waitFor(() =>
      expect(
        checklistItem(
          'Sie haben Fragen dieser Version freigegeben. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.',
        ),
      ).not.toBeNull(),
    );
  });

  test('refused by four eyes keeps the item while the version is read again unchanged, and drops it once its status changes', async () => {
    host.step = async () => refused(403, 'editorial.four-eyes');
    const { user, dialog, queryClient } = await openPublish();
    const fourEyes =
      'Sie haben diese Version eingereicht oder ihre Testvorgaben zuletzt geändert. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.';
    const readsBefore = reads().length;

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));
    await waitFor(() => expect(checklistItem(fourEyes)).not.toBeNull());
    // The refusal marks the version stale: read again, still in review, the item stays.
    await waitFor(() => expect(reads().length).toBeGreaterThan(readsBefore));
    await queryClient.refetchQueries();
    expect(checklistItem(fourEyes)).not.toBeNull();

    // Another editor returned it meanwhile: a draft again, the refusal no longer applies.
    showing(versionOf('Draft', { questions: complete() }));
    await queryClient.refetchQueries();

    await waitFor(() => expect(screen.getByText(statusText('Draft'))).toBeVisible());
    expect(checklist()).not.toBeNull();
    expect(checklistItem(fourEyes)).toBeNull();
  });

  test.each(dialogClosings)('stays open by %s while the host has not answered, and posts once', async (way) => {
    host.step = () => new Promise<Response>(() => undefined);
    const { user, dialog } = await openPublish();
    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Veröffentlichen' })).toBeDisabled());

    await tryToClose(dialog, way);

    expect(screen.getByRole('dialog', { name: 'Version veröffentlichen?' })).toBeVisible();
    expect(posts()).toHaveLength(1);
  });

  test.each(dialogClosings)('closes by %s while nothing is on its way, and posts nothing', async (way) => {
    const { dialog } = await openPublish();

    await tryToClose(dialog, way);

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(posts()).toEqual([]);
  });

  test('refused by four eyes keeps the item gone once the version was returned and submitted again', async () => {
    host.step = async () => refused(403, 'editorial.four-eyes');
    const { user, dialog, queryClient } = await openPublish();
    const fourEyes =
      'Sie haben diese Version eingereicht oder ihre Testvorgaben zuletzt geändert. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.';
    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));
    await waitFor(() => expect(checklistItem(fourEyes)).not.toBeNull());

    // Returned to draft, then submitted again: whoever submits it now, the old refusal says nothing about it.
    showing(versionOf('Draft', { questions: complete() }));
    await queryClient.refetchQueries();
    await waitFor(() => expect(screen.getByText(statusText('Draft'))).toBeVisible());
    expect(checklistItem(fourEyes)).toBeNull();
    showing(versionOf('InReview', { questions: complete() }));
    await queryClient.refetchQueries();

    await waitFor(() => expect(screen.getByText(statusText('InReview'))).toBeVisible());
    expect(checklistItem(fourEyes)).toBeNull();
    expect(within(checklist() ?? document.body).getAllByRole('listitem')).toHaveLength(4);
  });

  test('refused by four eyes drops the item on another version', async () => {
    host.step = async () => refused(403, 'editorial.four-eyes');
    const { user, dialog, router } = await openPublish();
    const fourEyes =
      'Sie haben diese Version eingereicht oder ihre Testvorgaben zuletzt geändert. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.';
    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));
    await waitFor(() => expect(checklistItem(fourEyes)).not.toBeNull());

    const other = '0198c1a0-0000-7000-8000-0000000000c9';
    await router.push(`/editorial/versions/${other}`);

    await waitFor(() => expect(reads().some(({ path }) => path === `/editorial/course-versions/${other}`)).toBe(true));
    await screen.findByRole('heading', { level: 1, name: title });
    await waitFor(() => expect(checklistItem('Die Version ist zur Prüfung eingereicht.')).not.toBeNull());
    expect(checklistItem(fourEyes)).toBeNull();
  });

  test('refused for an unapproved question says so', async () => {
    host.step = async () => refused(409, 'editorial.question-not-approved');
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('Jede Frage muss vor der Veröffentlichung freigegeben sein.'),
    );
  });

  test('refused for a defective question says one is incomplete', async () => {
    host.step = async () => refused(422, 'editorial.single-choice-one-correct');
    const { user, dialog } = await openPublish();

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Eine Frage ist unvollständig. Prüfen Sie im Test ihre Antwortoptionen und richtigen Antworten.',
      ),
    );
  });
});

describe('retiring a version', () => {
  test.each(['Published', 'Superseded'] as const)(
    '%s asks first, destructively, and posts nothing when cancelled',
    async (status) => {
      showing(versionOf(status));
      const user = userEvent.setup();
      await renderPage();

      await user.click(actionButton('retire') ?? document.body);

      const dialog = await screen.findByRole('dialog', { name: 'Version zurückziehen?' });
      expect(dialog).toHaveAccessibleDescription(
        'Version 2 von „Hygiene in der Apotheke“ wird zurückgezogen. Laufende Lerndurchläufe bleiben gültig.',
      );
      expect(within(dialog).getByRole('button', { name: 'Zurückziehen' })).toHaveClass('bg-destructive');
      await user.click(within(dialog).getByRole('button', { name: t('editorial.cancel') }));
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(posts()).toEqual([]);
    },
  );

  test.each(['Published', 'Superseded'] as const)(
    '%s, confirmed, posts, reports it and puts the focus on the heading once it is retired',
    async (status) => {
      showing(versionOf(status));
      const user = userEvent.setup();
      const { heading } = await renderPage();
      await user.click(actionButton('retire') ?? document.body);
      const dialog = await screen.findByRole('dialog', { name: 'Version zurückziehen?' });

      await user.click(within(dialog).getByRole('button', { name: 'Zurückziehen' }));

      await waitFor(() => expect(heading).toHaveFocus());
      expect(screen.getByText(statusText('Retired'))).toBeVisible();
      expect(posts()).toEqual([
        { method: 'POST', path: `/editorial/course-versions/${versionId}/retire`, body: undefined },
      ]);
      expect(notify.success).toHaveBeenCalledWith('Version 2 ist zurückgezogen.');
      // Retired, it offers no step and no checklist.
      for (const action of Object.keys(actions) as Action[]) {
        expect(actionButton(action), action).toBeNull();
      }
      expect(checklist()).toBeNull();
    },
  );

  test('keeps "Neue Version anlegen" shut while the retirement is on its way', async () => {
    showing(versionOf('Published'));
    host.step = () => new Promise<Response>(() => undefined);
    const user = userEvent.setup();
    await renderPage();
    await user.click(actionButton('retire') ?? document.body);
    const dialog = await screen.findByRole('dialog', { name: 'Version zurückziehen?' });

    await user.click(within(dialog).getByRole('button', { name: 'Zurückziehen' }));

    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Zurückziehen' })).toBeDisabled());
    expect(screen.getByRole('button', { name: t('editorial.version.newVersion'), hidden: true })).toBeDisabled();
  });

  test('retired meanwhile, says only a published version can be, with the focus on the heading', async () => {
    showing(versionOf('Published'));
    host.step = async () => {
      showing(versionOf('Retired'));
      return refused(409, 'editorial.not-published');
    };
    const user = userEvent.setup();
    const { heading } = await renderPage();
    await user.click(actionButton('retire') ?? document.body);
    const dialog = await screen.findByRole('dialog', { name: 'Version zurückziehen?' });

    await user.click(within(dialog).getByRole('button', { name: 'Zurückziehen' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('Nur eine veröffentlichte Version kann zurückgezogen werden.'),
    );
    await waitFor(() => expect(screen.getByText(statusText('Retired'))).toBeVisible());
    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  test('says so in English', async () => {
    setLanguage('en-GB', { remember: false });
    showing(versionOf('Published'));
    const user = userEvent.setup();
    await renderPage();
    await user.click(screen.getByRole('button', { name: 'Retire' }));

    const dialog = await screen.findByRole('dialog', { name: 'Retire this version?' });
    expect(dialog).toHaveAccessibleDescription(
      'Version 2 of "Hygiene in der Apotheke" will be retired. Learning sessions already running stay valid.',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Retire' }));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Version 2 has been retired.'));
  });
});

describe('a step failing on the network', () => {
  const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);

  /** From the step on, the host cannot be reached at all: the version is not read again either. */
  function offlineFromTheStep() {
    let offline = false;
    const online = answering(host);
    host.step = async () => {
      offline = true;
      throw new TypeError('Failed to fetch');
    };
    fetch.mockImplementation(async (input, init) => {
      if (offline) {
        throw new TypeError('Failed to fetch');
      }
      return online(input, init);
    });
  }

  test('keeps the version shown when it cannot be read again, with the focus on Publish', async () => {
    showing(versionOf('InReview', { questions: complete() }));
    offlineFromTheStep();
    const user = userEvent.setup();
    const { queryClient, heading } = await renderPage();
    await user.click(actionButton('publish') ?? document.body);
    const dialog = await screen.findByRole('dialog', { name: 'Version veröffentlichen?' });

    await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.workflow.errors.publishFailed')));
    await waitFor(() => expect(queryClient.getQueryState(versionKey)?.status).toBe('error'));
    expect(heading).toBeInTheDocument();
    expect(screen.queryByText(t('editorial.version.loadFailed'))).toBeNull();
    await waitFor(() => expect(actionButton('publish')).toHaveFocus());
  });

  test('reports it at once with a query client that retries, as the app does, and gives the focus back', async () => {
    offlineFromTheStep();
    // The app's client: a failed read is retried three times, about seven seconds in all.
    const queryClient = new QueryClient();
    onTestFinished(() => queryClient.cancelQueries());
    const user = userEvent.setup();
    await renderVersionPage(versionId, title, queryClient);

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.workflow.errors.submitFailed')));
    await waitFor(() => expect(actionButton('submit')).toHaveFocus());
    expect(actionButton('submit')).toBeEnabled();
  });
});

describe('a step refused for a reason that does not change the version', () => {
  // The step is answered, then every read fails as well: the app's client retries a failed read three times, about
  // seven seconds in all. Only a 409, a 422 or a module's refusal of a version still there is waited for.
  const answers: [string, () => Response][] = [
    ["an edge's 502", () => new Response('Bad Gateway', { status: 502 })],
    ["a policy's 403 without a code", () => refused(403)],
    ['a 404 without a code', () => new Response('', { status: 404 })],
    ['the 404 of a version that is gone', () => refused(404, 'editorial.course-version-not-found')],
  ];

  test.each(answers)(
    '%s is reported at once with a query client that retries, and frees the steps',
    async (_name, answer) => {
      let failing = false;
      const online = answering(host);
      host.step = async () => {
        failing = true;
        return answer();
      };
      fetch.mockImplementation(async (input, init) =>
        failing && (init?.method ?? 'GET') === 'GET'
          ? new Response('Bad Gateway', { status: 502 })
          : online(input, init),
      );
      // The app's client.
      const queryClient = new QueryClient();
      onTestFinished(() => queryClient.cancelQueries());
      const user = userEvent.setup();
      await renderVersionPage(versionId, title, queryClient);

      await user.click(actionButton('submit') ?? document.body);

      await waitFor(() => expect(notify.failure).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(actionButton('submit')).toBeEnabled());
      await waitFor(() => expect(actionButton('submit')).toHaveFocus());
    },
  );
});

describe('a refusal of a module the page has no words of its own for', () => {
  const unknown = () => refusedWith(409, { title: 'Die Version ist gesperrt.', code: 'editorial.something-new' });

  test("says the host's title", async () => {
    host.step = async () => unknown();
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Version ist gesperrt.'));
  });

  test("says the host's title in English too, until #147 translates it", async () => {
    setLanguage('en-GB', { remember: false });
    host.step = async () => unknown();
    const user = userEvent.setup();
    await renderPage();

    await user.click(actionButton('submit') ?? document.body);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Version ist gesperrt.'));
  });
});

describe('drafting the next version', () => {
  test('keeps Retire shut until the host has answered', async () => {
    showing(versionOf('Published'));
    let answer: (response: Response) => void = () => undefined;
    const online = answering(host);
    fetch.mockImplementation((input, init) =>
      init?.method === 'POST' && String(input).endsWith('/next')
        ? new Promise<Response>((resolve) => {
            answer = resolve;
          })
        : online(input, init),
    );
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.version.newVersion') }));

    await waitFor(() => expect(screen.getByRole('button', { name: t('editorial.version.newVersion') })).toBeDisabled());
    expect(actionButton('retire')).toBeDisabled();
    answer(refused(409, 'editorial.not-published'));
    await waitFor(() => expect(notify.failure).toHaveBeenCalled());
    await waitFor(() => expect(actionButton('retire')).toBeEnabled());
  });
});

test('a step answered after the editor moved to another version leaves the focus free there', async () => {
  showing(versionOf('InReview', { questions: complete() }));
  let answer: (response: Response) => void = () => undefined;
  host.step = () =>
    new Promise<Response>((resolve) => {
      answer = resolve;
    });
  const other = '0198c1a0-0000-7000-8000-0000000000c9';
  let readOther: () => void = () => undefined;
  const otherReadable = new Promise<void>((resolve) => {
    readOther = resolve;
  });
  const online = answering(host);
  fetch.mockImplementation(async (input, init) => {
    if ((init?.method ?? 'GET') === 'GET' && String(input).endsWith(other)) {
      await otherReadable;
    }
    return online(input, init);
  });
  const user = userEvent.setup();
  const { router } = await renderPage();
  await user.click(actionButton('publish') ?? document.body);
  const dialog = await screen.findByRole('dialog', { name: 'Version veröffentlichen?' });
  await user.click(within(dialog).getByRole('button', { name: 'Veröffentlichen' }));
  await waitFor(() => expect(posts()).toHaveLength(1));

  // The editor opens another version while the publish is on its way; the host refuses it before that one shows.
  await router.push(`/editorial/versions/${other}`);
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  answer(refused(409, 'editorial.not-in-review'));
  await waitFor(() => expect(notify.failure).toHaveBeenCalled());
  showing(versionOf('Draft'));
  host.step = async (step) => stepTaken(host, step);
  readOther();
  const heading = await screen.findByRole('heading', { level: 1, name: title });

  await user.click(actionButton('submit') ?? document.body);

  await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Version 2 ist zur Prüfung eingereicht.'));
  await waitFor(() => expect(heading).toHaveFocus());
});

test('publishing says so in English', async () => {
  setLanguage('en-GB', { remember: false });
  showing(versionOf('InReview', { questions: complete() }));
  const user = userEvent.setup();
  await renderPage();
  await user.click(screen.getByRole('button', { name: 'Publish' }));

  const dialog = await screen.findByRole('dialog', { name: 'Publish this version?' });
  expect(dialog).toHaveAccessibleDescription(
    'Version 2 of "Hygiene in der Apotheke" will be published and can no longer be changed. A previously published version of this course is superseded.',
  );
  await user.click(within(dialog).getByRole('button', { name: 'Publish' }));

  await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Version 2 has been published.'));
});

test('the components the steps are taken with are files of their own', () => {
  const files = import.meta.glob(['./PublishChecklist.vue', './ConfirmWorkflowDialog.vue']);

  expect(Object.keys(files).sort()).toEqual(['./ConfirmWorkflowDialog.vue', './PublishChecklist.vue']);
});
