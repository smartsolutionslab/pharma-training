import { render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import type { EditorialChapterDto, EditorialChapterRequest, EditorialQuestionDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import ChapterForm from './ChapterForm.vue';

// The form that adds a chapter or replaces one (spec 2.4), here a text chapter written in Markdown: its title
// (ChapterTitle: 1 to 150 characters, trimmed, no NUL) and its content (MarkdownText: 1 to 200,000 characters, not
// blank, no NUL), checked before the host checks them again. It hands the request to onSave; what saving answers, and
// what the page does then, are the page's.
const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const onSave = vi.fn<(request: EditorialChapterRequest) => Promise<boolean>>();

beforeEach(() => {
  onSave.mockReset();
  onSave.mockResolvedValue(true);
});

const introduction: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  text: '# Willkommen\n\nDas ist **wichtig**.',
  file: null,
  durationSeconds: null,
};

/** A question about the chapter, approved or not yet reviewed. */
function question(id: string, chapterId: string, review: 'Approved' | 'Unreviewed'): EditorialQuestionDto {
  return {
    id,
    position: 1,
    chapterId,
    text: 'Wie lange waschen Sie die Hände?',
    type: 'SingleChoice',
    options: [
      { id: `${id}-1`, text: '30 Sekunden', correct: true },
      { id: `${id}-2`, text: '5 Sekunden', correct: false },
    ],
    provenance: 'Human',
    origin: 'HandWritten',
    review,
    reviewedBy: null,
    reviewedAt: null,
    approvedByName: null,
    approvedAt: null,
  };
}

const titleField = () => screen.getByRole('textbox', { name: t('editorial.chapterForm.title') });
const textField = () => screen.getByRole('textbox', { name: t('editorial.chapterForm.text') });
const kinds = () => screen.getByRole('radiogroup', { name: t('editorial.chapterForm.kind') });
const kind = (name: 'Text' | 'Pdf' | 'Video') =>
  within(kinds()).getByRole('radio', { name: t(`glossary.chapterKind.${name}`) });
const submit = () => screen.getByRole('button', { name: t('editorial.chapterForm.submit') });
const writeToggle = () => screen.getByRole('button', { name: t('editorial.chapterForm.write') });
const previewToggle = () => screen.getByRole('button', { name: t('editorial.chapterForm.preview') });

type Props = { chapter?: EditorialChapterDto; questions?: EditorialQuestionDto[] };

function renderForm(props: Props = {}) {
  return render(ChapterForm, { props: { versionId, questions: [], ...props, onSave, saving: false } });
}

/** Fills the fields it is given (pasted, so a long text is quick), each replacing what the field held, and submits. */
async function enter({ title, text }: { title?: string; text?: string }, props: Props = {}) {
  const user = userEvent.setup();
  renderForm(props);
  if (title !== undefined) {
    await user.clear(titleField());
    await user.click(titleField());
    await user.paste(title);
  }
  if (text !== undefined) {
    await user.clear(textField());
    await user.click(textField());
    await user.paste(text);
  }
  await user.click(submit());
  return user;
}

test('a new chapter starts empty, as text, with a submit button', () => {
  renderForm();

  expect(titleField()).toHaveValue('');
  expect(kind('Text')).toBeChecked();
  expect(kind('Pdf')).not.toBeChecked();
  expect(kind('Video')).not.toBeChecked();
  expect(textField()).toHaveValue('');
  expect(textField().tagName).toBe('TEXTAREA');
  expect(submit()).toHaveAttribute('type', 'submit');
  expect(submit()).toBeEnabled();
});

test('a text chapter is saved with its trimmed title and its Markdown, and no file or duration', async () => {
  await enter({ title: '  Einleitung  ', text: '# Willkommen\n\nDas ist **wichtig**.' });

  await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  expect(onSave).toHaveBeenCalledWith({
    title: 'Einleitung',
    kind: 'Text',
    text: '# Willkommen\n\nDas ist **wichtig**.',
    file: null,
    durationSeconds: null,
  });
});

test.each([
  ['a blank title', { title: '   ', text: 'Inhalt' }, 'editorial.chapterForm.titleRequired', 'title'],
  [
    'a title of 151 characters',
    { title: 'A'.repeat(151), text: 'Inhalt' },
    'editorial.chapterForm.titleTooLong',
    'title',
  ],
  ['a title with a NUL', { title: 'Ein\u0000leitung', text: 'Inhalt' }, 'editorial.chapterForm.titleHasNul', 'title'],
  ['no content', { title: 'Einleitung' }, 'editorial.chapterForm.textRequired', 'text'],
  ['blank content', { title: 'Einleitung', text: '  \n  ' }, 'editorial.chapterForm.textRequired', 'text'],
  ['content with a NUL', { title: 'Einleitung', text: 'In\u0000halt' }, 'editorial.chapterForm.textHasNul', 'text'],
] as const)('%s is refused at its field, and nothing is saved', async (_case, values, message, field) => {
  await enter(values);

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t(message));
  const invalid = field === 'title' ? titleField() : textField();
  expect(invalid).toHaveAttribute('aria-invalid', 'true');
  expect(invalid).toHaveAccessibleDescription(t(message));
  expect(onSave).not.toHaveBeenCalled();
});

test('a title of 150 characters is saved', async () => {
  await enter({ title: 'A'.repeat(150), text: 'Inhalt' });

  await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  expect(onSave.mock.calls[0]?.[0].title).toBe('A'.repeat(150));
});

test('content of more than 200,000 characters is refused, naming the limit as the language writes it', async () => {
  await enter({ title: 'Einleitung', text: 'a'.repeat(200_001) });

  expect(await screen.findByRole('alert')).toHaveTextContent(
    t('editorial.chapterForm.textTooLong', { max: '200.000' }),
  );
  expect(screen.getByRole('alert')).toHaveTextContent('Der Inhalt eines Kapitels hat höchstens 200.000 Zeichen.');
  expect(onSave).not.toHaveBeenCalled();
});

test('the German messages themselves', async () => {
  await enter({ title: '   ' });

  const alerts = await screen.findAllByRole('alert');
  expect(alerts.map((alert) => alert.textContent.trim())).toEqual([
    'Geben Sie dem Kapitel einen Titel.',
    'Schreiben Sie den Inhalt des Kapitels.',
  ]);
});

test('the fields are named in German', () => {
  renderForm();

  expect(screen.getByRole('textbox', { name: 'Titel' })).toBeInTheDocument();
  const kindsInGerman = within(screen.getByRole('radiogroup', { name: 'Art' }));
  expect(kindsInGerman.getAllByRole('radio')).toEqual([
    kindsInGerman.getByRole('radio', { name: 'Text' }),
    kindsInGerman.getByRole('radio', { name: 'PDF' }),
    kindsInGerman.getByRole('radio', { name: 'Video' }),
  ]);
  expect(screen.getByRole('textbox', { name: 'Inhalt (Markdown)' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Schreiben' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Vorschau' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Speichern' })).toBeInTheDocument();
});

// The Markdown editor (MarkdownEditor.vue): a textarea and a preview rendered through MarkdownView, behind two toggles.
test('the editor writes first, and its preview shows the Markdown rendered and sanitised', async () => {
  const user = userEvent.setup();
  renderForm();
  expect(writeToggle()).toHaveAttribute('aria-pressed', 'true');
  expect(previewToggle()).toHaveAttribute('aria-pressed', 'false');
  await user.click(textField());
  await user.paste('# Willkommen\n\nDas ist **wichtig**.\n\n<script>window.hacked = true</script>');

  await user.click(previewToggle());

  expect(previewToggle()).toHaveAttribute('aria-pressed', 'true');
  expect(writeToggle()).toHaveAttribute('aria-pressed', 'false');
  const strong = await screen.findByText('wichtig', { selector: 'strong' });
  const preview = strong.closest('.markdown');
  expect(preview).not.toBeNull();
  expect(preview?.querySelector('h1')).toBeNull();
  expect(preview?.querySelector('script')).toBeNull();
  expect(preview?.textContent).toContain('<script>window.hacked = true</script>');
});

test('the preview of nothing says there is nothing to preview, and writing again keeps the text', async () => {
  const user = userEvent.setup();
  renderForm();

  await user.click(previewToggle());

  expect(await screen.findByText(t('editorial.chapterForm.previewEmpty'))).toBeVisible();
  expect(screen.getByText('Noch nichts zur Vorschau.')).toBeVisible();

  await user.click(writeToggle());
  await user.click(textField());
  await user.paste('Etwas');
  await user.click(previewToggle());
  await user.click(writeToggle());

  expect(textField()).toHaveValue('Etwas');
});

test('editing fills the form with the chapter, and saving sends what was changed with what was not', async () => {
  const user = userEvent.setup();
  renderForm({ chapter: introduction });

  expect(titleField()).toHaveValue('Einleitung');
  expect(kind('Text')).toBeChecked();
  expect(textField()).toHaveValue(introduction.text);

  await user.clear(titleField());
  await user.click(titleField());
  await user.paste('Einführung');
  await user.click(submit());

  await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  expect(onSave).toHaveBeenCalledWith({
    title: 'Einführung',
    kind: 'Text',
    text: introduction.text,
    file: null,
    durationSeconds: null,
  });
});

// Replacing a chapter makes its questions Unreviewed again (CourseVersion.ReplaceChapter): the form says so beforehand.
test('editing a chapter with approved questions says they need reviewing again, counting the approved ones', () => {
  renderForm({
    chapter: introduction,
    questions: [
      question('0198c1a0-0000-7000-8000-0000000000a1', introduction.id, 'Approved'),
      question('0198c1a0-0000-7000-8000-0000000000a2', introduction.id, 'Approved'),
      question('0198c1a0-0000-7000-8000-0000000000a3', introduction.id, 'Unreviewed'),
      question('0198c1a0-0000-7000-8000-0000000000a4', '0198c1a0-0000-7000-8000-0000000000e9', 'Approved'),
    ],
  });

  expect(screen.getByText(t('editorial.chapterForm.requalify', { count: 2 }, 2))).toBeInTheDocument();
  expect(
    screen.getByText('Nach dem Speichern müssen 2 freigegebene Fragen zu diesem Kapitel erneut geprüft werden.'),
  ).toBeInTheDocument();
});

test('one approved question is named in the singular', () => {
  renderForm({
    chapter: introduction,
    questions: [question('0198c1a0-0000-7000-8000-0000000000a1', introduction.id, 'Approved')],
  });

  expect(
    screen.getByText('Nach dem Speichern muss eine freigegebene Frage zu diesem Kapitel erneut geprüft werden.'),
  ).toBeInTheDocument();
});

test.each([
  ['a new chapter', {}],
  [
    'a chapter whose questions are all unreviewed',
    {
      chapter: introduction,
      questions: [question('0198c1a0-0000-7000-8000-0000000000a3', introduction.id, 'Unreviewed')],
    },
  ],
])('%s says nothing about reviewing again', (_case, props: Props) => {
  renderForm(props);

  expect(screen.queryByText(/erneut geprüft/)).not.toBeInTheDocument();
});

test('switched to English, the form speaks English', async () => {
  renderForm({
    chapter: introduction,
    questions: [question('0198c1a0-0000-7000-8000-0000000000a1', introduction.id, 'Approved')],
  });

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('textbox', { name: 'Content (Markdown)' })).toBeInTheDocument();
  expect(screen.getByRole('radiogroup', { name: 'Type' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  expect(
    screen.getByText('After saving, one approved question about this chapter needs reviewing again.'),
  ).toBeInTheDocument();
});
