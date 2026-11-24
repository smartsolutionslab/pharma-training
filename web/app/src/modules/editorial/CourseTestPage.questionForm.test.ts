import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialCourseVersionStatus } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import {
  approvedAi,
  handout,
  introduction,
  questionsUpTo,
  refusal,
  unreviewedAi,
  versionId,
  versionOf,
} from './courseTestFixtures';

// Adding, editing and deleting a question of a draft (#25, S5 and T5): a form in a dialog with the question's chapter,
// text, type and options, checked as approval checks them before anything is sent; an edit makes the question
// unreviewed again, and the form says so before an approved one is saved; a question is deleted after confirming.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host: GET /editorial/course-versions/{id} answers the version; a change (POST …/questions, PUT and DELETE
// …/questions/{qid}) answers what the test says, by default as the host would.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;
let changing: (method: string, path: string) => Promise<Response>;

const versionPath = `/api/editorial/course-versions/${versionId}`;
const newQuestionId = '0198c1a0-0000-7000-8000-0000000000a9';

beforeEach(() => {
  version = versionOf('Draft');
  changing = async (method) =>
    method === 'POST' ? Response.json(newQuestionId, { status: 201 }) : Response.json({ value: null });
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === versionPath) {
      return Response.json(version);
    }
    if (method !== 'GET' && url.pathname.startsWith(`${versionPath}/questions`)) {
      return changing(method, url.pathname);
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(cleanup);

/** What the page sent to change the version, as the host received it. */
const changes = () =>
  host.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') !== 'GET')
    .map(([input, init]) => ({
      method: init?.method,
      path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
      body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
    }));
const reads = () => host.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET');

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

type Scope = ReturnType<typeof within>;
type User = ReturnType<typeof userEvent.setup>;

const addButton = () => screen.getByRole('button', { name: t('editorial.test.questions.add') });
const formDialog = async (title: 'addTitle' | 'editTitle') =>
  within(await screen.findByRole('dialog', { name: t(`editorial.test.questionForm.${title}`) }));

const chapterField = (dialog: Scope) =>
  dialog.getByRole('combobox', { name: t('editorial.test.questionForm.chapter') }) as HTMLSelectElement;
const textField = (dialog: Scope) => dialog.getByRole('textbox', { name: t('editorial.test.questionForm.text') });
const typeRadio = (dialog: Scope, type: 'SingleChoice' | 'MultipleChoice') =>
  dialog.getByRole('radio', { name: t(`glossary.questionType.${type}`) });
const optionField = (dialog: Scope, number: number) =>
  dialog.getByRole('textbox', { name: t('editorial.test.questionForm.option', { number }) });
/** The options' text fields, Option 1, Option 2, … in both languages. */
const optionFields = (dialog: Scope) => dialog.queryAllByRole('textbox', { name: /^Option \d+$/ });
/** A group of radios by its name: a radio group, or a fieldset (role group) around native radios. */
const groupNamed = (dialog: Scope, name: string) =>
  dialog.queryByRole('radiogroup', { name }) ?? dialog.getByRole('group', { name });
/** The single-choice radios that pick the correct option, in the options' order. */
const correctRadios = (dialog: Scope) =>
  within(groupNamed(dialog, t('editorial.test.questionForm.correctOption'))).getAllByRole('radio');
const correctRadio = (dialog: Scope, index: number) => {
  const radio = correctRadios(dialog)[index];
  expect(radio).toBeDefined();
  return radio ?? document.body;
};
const correctBox = (dialog: Scope, number: number) =>
  dialog.getByRole('checkbox', { name: t('editorial.test.questionForm.optionCorrect', { number }) });
const submit = (dialog: Scope) => dialog.getByRole('button', { name: t('editorial.test.questionForm.submit') });
const addOption = (dialog: Scope) => dialog.queryByRole('button', { name: t('editorial.test.questionForm.addOption') });
const removeOption = (dialog: Scope, number: number) =>
  dialog.queryByRole('button', { name: t('editorial.test.questionForm.removeOption', { number }) });
/** A control that is not offered: absent, or there but disabled. */
const unavailable = (control: HTMLElement | null) => control === null || control.hasAttribute('disabled');

/** Replaces what a field holds with the text, pasted. */
async function write(user: User, field: HTMLElement, text: string) {
  await user.clear(field);
  await user.click(field);
  await user.paste(text);
}

/** The message the form shows for a field, as an alert. */
async function message(dialog: Scope, key: Parameters<typeof t>[0], named: Record<string, unknown> = {}) {
  const shown = await dialog.findAllByText(t(key, named));
  expect(shown.length).toBeGreaterThan(0);
  for (const each of shown) {
    expect(each.closest('[role="alert"]')).not.toBeNull();
  }
  return shown;
}

describe('adding a question', () => {
  test('a single-choice question is added with its chapter, text and two options, one correct', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    expect(typeRadio(dialog, 'SingleChoice')).toBeChecked();
    expect(optionFields(dialog)).toHaveLength(2);
    await user.selectOptions(chapterField(dialog), introduction.id);
    await write(user, textField(dialog), '  Wie warm ist das Wasser beim Händewaschen?  ');
    await write(user, optionField(dialog, 1), 'Lauwarm ');
    await write(user, optionField(dialog, 2), 'Eiskalt');
    await user.click(correctRadio(dialog, 0));
    await user.click(submit(dialog));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('editorial.test.questions.saved')));
    expect(notify.success).toHaveBeenCalledWith('Die Frage wurde gespeichert.');
    expect(changes()).toEqual([
      {
        method: 'POST',
        path: `/editorial/course-versions/${versionId}/questions`,
        body: {
          chapterId: introduction.id,
          text: 'Wie warm ist das Wasser beim Händewaschen?',
          type: 'SingleChoice',
          options: [
            { text: 'Lauwarm', correct: true },
            { text: 'Eiskalt', correct: false },
          ],
        },
      },
    ]);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('the dialog is named Neue Frage and its chapter is chosen from the version’s chapters by position', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { chapters: [handout, introduction] });
    await renderPage();

    await user.click(screen.getByRole('button', { name: 'Frage hinzufügen' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Neue Frage' }));

    expect([...chapterField(dialog).options].map((option) => option.textContent?.trim())).toEqual([
      t('editorial.test.questionForm.chooseChapter'),
      'Kapitel 1: Einleitung',
      'Kapitel 2: Handout',
    ]);
    expect(chapterField(dialog).options[0]?.textContent?.trim()).toBe('Bitte wählen…');
    expect(chapterField(dialog).value).toBe('');
  });

  test('from the empty state opens the same form', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { questions: [] });
    await renderPage();

    const sentence = await screen.findByText(t('editorial.test.questions.empty'));
    const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
    await user.click(emptyState.getByRole('button', { name: t('editorial.test.questions.add') }));

    expect(await screen.findByRole('dialog', { name: 'Neue Frage' })).toBeInTheDocument();
  });

  test('an empty form is refused with each field’s message before anything is sent', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    await user.click(submit(dialog));

    await message(dialog, 'editorial.test.questionForm.chapterRequired');
    expect(dialog.getByText('Wählen Sie das Kapitel, auf das sich die Frage bezieht.')).toBeInTheDocument();
    await message(dialog, 'editorial.test.questionForm.textRequired');
    expect(dialog.getByText('Schreiben Sie die Frage.')).toBeInTheDocument();
    expect(await message(dialog, 'editorial.test.questionForm.optionRequired')).toHaveLength(2);
    await message(dialog, 'editorial.test.questionForm.singleChoiceOneCorrect');
    expect(dialog.getByText('Bei Single Choice ist genau eine Option richtig.')).toBeInTheDocument();
    expect(changes()).toEqual([]);
    expect(screen.getByRole('dialog', { name: t('editorial.test.questionForm.addTitle') })).toBeInTheDocument();
  });

  test('a single-choice question without a correct option is refused before sending', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    await user.selectOptions(chapterField(dialog), introduction.id);
    await write(user, textField(dialog), 'Wie warm ist das Wasser?');
    await write(user, optionField(dialog, 1), 'Lauwarm');
    await write(user, optionField(dialog, 2), 'Eiskalt');
    await user.click(submit(dialog));

    await message(dialog, 'editorial.test.questionForm.singleChoiceOneCorrect');
    expect(changes()).toEqual([]);
  });

  test('a multiple-choice question needs a correct option, and is sent with the ones ticked', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    await user.click(typeRadio(dialog, 'MultipleChoice'));
    await user.selectOptions(chapterField(dialog), handout.id);
    await write(user, textField(dialog), 'Was steht im Handout?');
    await write(user, optionField(dialog, 1), 'Die Öffnungszeiten');
    await write(user, optionField(dialog, 2), 'Die Hygieneregeln');
    await user.click(submit(dialog));

    await message(dialog, 'editorial.test.questionForm.multipleChoiceACorrect');
    expect(dialog.getByText('Bei Multiple Choice ist mindestens eine Option richtig.')).toBeInTheDocument();
    expect(changes()).toEqual([]);

    await user.click(correctBox(dialog, 2));
    expect(dialog.getByRole('checkbox', { name: 'Option 2 ist richtig' })).toBeChecked();
    await user.click(submit(dialog));

    await waitFor(() => expect(changes()).toHaveLength(1));
    expect(changes()[0]?.body).toEqual({
      chapterId: handout.id,
      text: 'Was steht im Handout?',
      type: 'MultipleChoice',
      options: [
        { text: 'Die Öffnungszeiten', correct: false },
        { text: 'Die Hygieneregeln', correct: true },
      ],
    });
  });

  test('switching from multiple to single choice keeps the first correct option only', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    await user.click(typeRadio(dialog, 'MultipleChoice'));
    const more = addOption(dialog);
    expect(more).not.toBeNull();
    await user.click(more ?? document.body);
    await user.selectOptions(chapterField(dialog), introduction.id);
    await write(user, textField(dialog), 'Wie lange waschen Sie die Hände?');
    await write(user, optionField(dialog, 1), '5 Sekunden');
    await write(user, optionField(dialog, 2), '30 Sekunden');
    await write(user, optionField(dialog, 3), 'Eine Minute');
    await user.click(correctBox(dialog, 2));
    await user.click(correctBox(dialog, 3));
    await user.click(typeRadio(dialog, 'SingleChoice'));

    expect(correctRadio(dialog, 0)).not.toBeChecked();
    expect(correctRadio(dialog, 1)).toBeChecked();
    expect(correctRadio(dialog, 2)).not.toBeChecked();
    await user.click(submit(dialog));

    await waitFor(() => expect(changes()).toHaveLength(1));
    expect(changes()[0]?.body).toEqual({
      chapterId: introduction.id,
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { text: '5 Sekunden', correct: false },
        { text: '30 Sekunden', correct: true },
        { text: 'Eine Minute', correct: false },
      ],
    });
  });

  test('options go from 2 to 10 and no further, and back down to 2 and no further', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    expect(optionFields(dialog)).toHaveLength(2);
    expect(unavailable(removeOption(dialog, 1))).toBe(true);
    expect(unavailable(removeOption(dialog, 2))).toBe(true);

    for (let count = 3; count <= 10; count++) {
      await user.click(addOption(dialog) ?? document.body);
      await waitFor(() => expect(optionFields(dialog)).toHaveLength(count));
    }
    expect(dialog.getByRole('textbox', { name: 'Option 10' })).toBeInTheDocument();
    expect(unavailable(addOption(dialog))).toBe(true);

    const removing = removeOption(dialog, 10);
    expect(unavailable(removing)).toBe(false);
    expect(dialog.getByRole('button', { name: 'Option 10 entfernen' })).toBe(removing);
    await user.click(removing ?? document.body);

    await waitFor(() => expect(optionFields(dialog)).toHaveLength(9));
    expect(unavailable(addOption(dialog))).toBe(false);
    expect(dialog.getByRole('button', { name: 'Option hinzufügen' })).toBeInTheDocument();
  });

  test('a form left with Abbrechen sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(addButton());
    const dialog = await formDialog('addTitle');
    await user.click(dialog.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(changes()).toEqual([]);
  });

  test('a test of 49 questions offers one more, and one of 50 none', async () => {
    version = versionOf('Draft', { questions: questionsUpTo(49) });
    const { unmount } = await renderPage();

    expect(addButton()).toBeEnabled();
    unmount();

    version = versionOf('Draft', { questions: questionsUpTo(50) });
    await renderPage();

    expect(screen.queryByRole('button', { name: t('editorial.test.questions.add') })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Frage hinzufügen' })).not.toBeInTheDocument();
  });

  test('the form speaks English when switched', async () => {
    const user = userEvent.setup();
    await renderPage();

    setLanguage('en-GB', { remember: false });
    const add = await screen.findByRole('button', { name: 'Add a question' });
    await user.click(add);

    const dialog = within(await screen.findByRole('dialog', { name: 'New question' }));
    expect(dialog.getByRole('combobox', { name: 'Chapter' })).toBeInTheDocument();
    expect(dialog.getByRole('textbox', { name: 'Question' })).toBeInTheDocument();
    expect(dialog.getByRole('radio', { name: 'Single choice' })).toBeChecked();
    expect(dialog.getByRole('radio', { name: 'Multiple choice' })).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add an option' })).toBeInTheDocument();
    await user.click(dialog.getByRole('button', { name: 'Save' }));
    expect(await dialog.findByText('Choose the chapter the question is about.')).toBeInTheDocument();
  });
});

describe('editing a question', () => {
  test('editing an approved question warns that it must be approved again, then puts it in its place', async () => {
    const user = userEvent.setup();
    changing = async (method) => {
      // The host makes an edited question unreviewed again.
      version = {
        ...version,
        questions: version.questions.map((question) =>
          question.id === approvedAi.id
            ? { ...question, text: 'Wie lange waschen Sie mindestens die Hände?', review: 'Unreviewed' as const }
            : question,
        ),
      };
      return method === 'PUT' ? Response.json({ value: null }) : new Response('', { status: 404 });
    };
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.test.questions.edit', { position: 1 }) }));
    const dialog = await formDialog('editTitle');
    expect(chapterField(dialog).value).toBe(introduction.id);
    expect(textField(dialog)).toHaveValue(approvedAi.text);
    expect(typeRadio(dialog, 'SingleChoice')).toBeChecked();
    expect(optionField(dialog, 1)).toHaveValue('30 Sekunden');
    expect(optionField(dialog, 2)).toHaveValue('5 Sekunden');
    expect(correctRadio(dialog, 0)).toBeChecked();
    expect(correctRadio(dialog, 1)).not.toBeChecked();
    expect(dialog.getByText(t('editorial.test.questionForm.reReview'))).toBeInTheDocument();
    expect(dialog.getByText('Nach dem Speichern muss die Frage erneut geprüft werden.')).toBeInTheDocument();

    await write(user, textField(dialog), 'Wie lange waschen Sie mindestens die Hände?');
    await user.click(submit(dialog));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('editorial.test.questions.saved')));
    expect(changes()).toEqual([
      {
        method: 'PUT',
        path: `/editorial/course-versions/${versionId}/questions/${approvedAi.id}`,
        body: {
          chapterId: introduction.id,
          text: 'Wie lange waschen Sie mindestens die Hände?',
          type: 'SingleChoice',
          options: [
            { text: '30 Sekunden', correct: true },
            { text: '5 Sekunden', correct: false },
          ],
        },
      },
    ]);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Read again, the edited question is unreviewed.
    const list = screen.getByRole('list', { name: t('editorial.test.questions.title') });
    await waitFor(() => expect(within(list).getAllByText('Ungeprüft')).toHaveLength(3));
  });

  test('the edit dialog is named Frage bearbeiten; an unreviewed question gives no warning', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: 'Frage 2 bearbeiten' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Frage bearbeiten' }));

    expect(typeRadio(dialog, 'MultipleChoice')).toBeChecked();
    expect(optionFields(dialog)).toHaveLength(3);
    expect(correctBox(dialog, 1)).toBeChecked();
    expect(correctBox(dialog, 2)).toBeChecked();
    expect(correctBox(dialog, 3)).not.toBeChecked();
    expect(dialog.queryByText('Nach dem Speichern muss die Frage erneut geprüft werden.')).not.toBeInTheDocument();
  });

  test.each([
    [
      'a 409 of the module',
      () => refusal(409, 'editorial.not-a-draft', 'SERVER WORDS'),
      () => 'Nur ein Entwurf kann bearbeitet werden.',
    ],
    ['a 500', () => new Response('', { status: 500 }), () => t('editorial.test.questions.saveFailed')],
    ['a 502 of the edge', () => new Response('', { status: 502 }), () => t('editorial.test.questions.saveFailed')],
    ['an unknown module code', () => refusal(409, 'editorial.zzz-unknown', 'SERVER WORDS'), () => 'SERVER WORDS'],
  ])(
    'a question the host refuses with %s is reported, the dialog stays open with what was entered, and the version is read again',
    async (_answer, answer, reported) => {
      const user = userEvent.setup();
      changing = async () => answer();
      await renderPage();

      await user.click(screen.getByRole('button', { name: t('editorial.test.questions.edit', { position: 2 }) }));
      const dialog = await formDialog('editTitle');
      await write(user, textField(dialog), 'Was gehört alles zur Händehygiene?');
      await user.click(submit(dialog));

      await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(reported()));
      expect(notify.failure).toHaveBeenCalledOnce();
      expect(notify.success).not.toHaveBeenCalled();
      expect(screen.getByRole('dialog', { name: t('editorial.test.questionForm.editTitle') })).toBeInTheDocument();
      expect(textField(dialog)).toHaveValue('Was gehört alles zur Händehygiene?');
      await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    },
  );

  test('a save that failed says so in German', async () => {
    const user = userEvent.setup();
    changing = async () => new Response('', { status: 500 });
    await renderPage();

    await user.click(screen.getByRole('button', { name: 'Frage 2 bearbeiten' }));
    const dialog = await formDialog('editTitle');
    await user.click(submit(dialog));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Frage konnte nicht gespeichert werden.'));
  });
});

describe('deleting a question', () => {
  /** The dialog that asks before a question is deleted, an alert dialog or a dialog. */
  const confirmation = () =>
    screen.queryByRole('alertdialog', { name: t('editorial.test.questions.removeConfirm.title') }) ??
    screen.queryByRole('dialog', { name: t('editorial.test.questions.removeConfirm.title') });

  async function openConfirmation(user: User, position: number) {
    await user.click(screen.getByRole('button', { name: t('editorial.test.questions.remove', { position }) }));
    await waitFor(() => expect(confirmation()).not.toBeNull());
    return within(confirmation() ?? document.body);
  }

  test('a question is deleted after confirming, reported and the version read again', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user, 2);
    expect(
      dialog.getByText(t('editorial.test.questions.removeConfirm.text', { text: unreviewedAi.text })),
    ).toBeInTheDocument();
    expect(dialog.getByText('„Was gehört zur Händehygiene?“ wird gelöscht.')).toBeInTheDocument();
    expect(changes()).toEqual([]);
    await user.click(dialog.getByRole('button', { name: t('editorial.test.questions.removeConfirm.confirm') }));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('editorial.test.questions.removed')));
    expect(notify.success).toHaveBeenCalledWith('Die Frage wurde gelöscht.');
    expect(changes()).toEqual([
      {
        method: 'DELETE',
        path: `/editorial/course-versions/${versionId}/questions/${unreviewedAi.id}`,
        body: undefined,
      },
    ]);
    await waitFor(() => expect(confirmation()).toBeNull());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });

  test('the confirmation is named Frage löschen? and confirmed with Löschen', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: 'Frage 1 löschen' }));
    const named = () =>
      screen.queryByRole('alertdialog', { name: 'Frage löschen?' }) ??
      screen.queryByRole('dialog', { name: 'Frage löschen?' });
    await waitFor(() => expect(named()).not.toBeNull());

    expect(within(named() ?? document.body).getByRole('button', { name: 'Löschen' })).toBeInTheDocument();
  });

  test('a long question is named by its first 80 characters', async () => {
    const user = userEvent.setup();
    const text =
      'Welche Regel gilt für die Händedesinfektion vor jedem Kontakt mit Kundinnen und Kunden in der Offizin?';
    expect(text.length).toBeGreaterThan(80);
    version = versionOf('Draft', { questions: [{ ...unreviewedAi, position: 1, text }] });
    await renderPage();

    const dialog = await openConfirmation(user, 1);

    const description = dialog.getByText((content) => content.includes(text.slice(0, 80)));
    expect(description).toBeInTheDocument();
    expect(description.textContent).not.toContain(text);
  });

  test('a confirmation left with Abbrechen deletes nothing', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user, 2);
    await user.click(dialog.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(confirmation()).toBeNull());
    expect(changes()).toEqual([]);
  });

  test.each([
    [
      'a 409 of the module',
      () => refusal(409, 'editorial.not-a-draft', 'SERVER WORDS'),
      () => 'Nur ein Entwurf kann bearbeitet werden.',
    ],
    ['a 500', () => new Response('', { status: 500 }), () => t('editorial.test.questions.removeFailed')],
  ])(
    'a deletion the host refuses with %s is reported and the version read again',
    async (_answer, answer, reported) => {
      const user = userEvent.setup();
      changing = async () => answer();
      await renderPage();

      const dialog = await openConfirmation(user, 2);
      await user.click(dialog.getByRole('button', { name: t('editorial.test.questions.removeConfirm.confirm') }));

      await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(reported()));
      expect(notify.success).not.toHaveBeenCalled();
      await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    },
  );

  test('a deletion that failed says so in German', async () => {
    const user = userEvent.setup();
    changing = async () => new Response('', { status: 500 });
    await renderPage();

    const dialog = await openConfirmation(user, 2);
    await user.click(dialog.getByRole('button', { name: 'Löschen' }));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Frage konnte nicht gelöscht werden.'));
  });
});

describe('a version that is not a draft', () => {
  test.each<EditorialCourseVersionStatus>(['InReview', 'Published', 'Superseded', 'Retired'])(
    'a non-draft (%s) offers no question to add, edit or delete',
    async (status) => {
      version = versionOf(status);
      await renderPage();

      expect(screen.queryByRole('button', { name: t('editorial.test.questions.add') })).not.toBeInTheDocument();
      for (const position of [1, 2, 3]) {
        expect(
          screen.queryByRole('button', { name: t('editorial.test.questions.edit', { position }) }),
        ).not.toBeInTheDocument();
        expect(
          screen.queryByRole('button', { name: t('editorial.test.questions.remove', { position }) }),
        ).not.toBeInTheDocument();
      }
      expect(screen.queryByRole('button', { name: /^Frage \d bearbeiten$/ })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /^Frage \d löschen$/ })).not.toBeInTheDocument();
    },
  );

  test('a draft offers edit and delete on every question', async () => {
    await renderPage();

    for (const position of [1, 2, 3]) {
      expect(screen.getByRole('button', { name: `Frage ${position} bearbeiten` })).toBeEnabled();
      expect(screen.getByRole('button', { name: `Frage ${position} löschen` })).toBeEnabled();
    }
  });
});
