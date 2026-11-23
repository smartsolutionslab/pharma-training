import { render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import CreateCourseForm from './CreateCourseForm.vue';

// The form that starts a course: its title, checked here (CourseTitle: 1 to 150 characters, trimmed, no NUL) before the
// host checks it again. What creating answers, and what the page does then, are the page's.
const onCreate = vi.fn<(title: string) => Promise<boolean>>();

beforeEach(() => {
  onCreate.mockReset();
  onCreate.mockResolvedValue(true);
});

const titleField = () => screen.getByRole('textbox', { name: t('editorial.course.form.title') });
const submit = () => screen.getByRole('button', { name: t('editorial.course.form.submit') });

/** Enters a title (pasted, so a long one is quick) and submits. */
async function enter(title: string) {
  const user = userEvent.setup();
  render(CreateCourseForm, { props: { onCreate, saving: false } });
  await user.click(titleField());
  await user.paste(title);
  await user.click(submit());
}

test('the form asks for the title and creates with a submit button', () => {
  render(CreateCourseForm, { props: { onCreate, saving: false } });

  expect(titleField()).toHaveValue('');
  expect(submit()).toHaveAttribute('type', 'submit');
  expect(submit()).toBeEnabled();
});

test('a blank title is refused below the field, and nothing is created', async () => {
  await enter('   ');

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('editorial.course.form.titleRequired'));
  expect(titleField()).toHaveAttribute('aria-invalid', 'true');
  expect(titleField()).toHaveAccessibleDescription(t('editorial.course.form.titleRequired'));
  expect(onCreate).not.toHaveBeenCalled();
});

test('a title of 151 characters is refused, and nothing is created', async () => {
  await enter('A'.repeat(151));

  expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.course.form.titleTooLong'));
  expect(onCreate).not.toHaveBeenCalled();
});

test('a title of 150 characters is created', async () => {
  await enter('A'.repeat(150));

  await waitFor(() => expect(onCreate).toHaveBeenCalledWith('A'.repeat(150)));
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('a title with a nul character is refused, and nothing is created', async () => {
  await enter('Hygiene\u0000II');

  expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.course.form.titleHasNul'));
  expect(onCreate).not.toHaveBeenCalled();
});

test('a valid title is created trimmed', async () => {
  await enter('  Hygiene in der Apotheke  ');

  await waitFor(() => expect(onCreate).toHaveBeenCalledWith('Hygiene in der Apotheke'));
  expect(onCreate).toHaveBeenCalledOnce();
});

test('while the course is being created the button is disabled', () => {
  render(CreateCourseForm, { props: { onCreate, saving: true } });

  expect(submit()).toBeDisabled();
});

// The German texts themselves, not only their keys.
test('the form and its refusals speak German by default', async () => {
  const user = userEvent.setup();
  render(CreateCourseForm, { props: { onCreate, saving: false } });

  expect(screen.getByRole('textbox', { name: 'Titel' })).toBe(titleField());
  expect(screen.getByRole('button', { name: 'Anlegen' })).toBe(submit());
  await user.click(submit());
  expect(await screen.findByRole('alert')).toHaveTextContent('Geben Sie der Schulung einen Titel.');

  await user.paste('A'.repeat(151));
  await user.click(submit());
  expect(await screen.findByText('Der Titel einer Schulung hat höchstens 150 Zeichen.')).toHaveAttribute(
    'role',
    'alert',
  );
});

test('a refusal shown follows a switch to English', async () => {
  await enter('   ');
  expect(await screen.findByRole('alert')).toHaveTextContent('Geben Sie der Schulung einen Titel.');

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByText('Give the course a title.')).toHaveAttribute('role', 'alert');
  expect(screen.getByRole('textbox', { name: 'Title' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument();
});
