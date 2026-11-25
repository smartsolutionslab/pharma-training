import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { versionId, versionOf } from './courseTestFixtures';
import {
  answering,
  dialogClosings,
  type Fetch,
  refused,
  renderVersionPage,
  requestsOf,
  stepTaken,
  tryToClose,
  type WorkflowHost,
  workflowHost,
} from './workflowPageHost';

// Returning a version in review to draft (#26, ADR 0013), from the course version page: a dialog asks for the reason,
// with a hint beside the field not to name people (the owner's decision), checks it as the host does (trimmed, 1 to
// 1000 characters, plain text) and posts { reason } trimmed. A reason the host refuses (400 invalid-return-reason) keeps
// the dialog open with the field's message and the focus in it; any other refusal closes it and is reported.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: WorkflowHost;

beforeEach(() => {
  host = workflowHost(versionOf('InReview'));
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const title = 'Hygiene in der Apotheke';
const hint = 'Bitte nennen Sie im Grund keine Personen, sondern beschreiben Sie, was an der Version zu ändern ist.';

/** The host's whole rule for a reason, as the field states it when the host refuses one (#201). */
const reasonRule = {
  de: 'Der Grund muss ohne Leerraum am Anfang und Ende 1 bis 1000 Zeichen lang sein und darf außer Zeilenumbrüchen und Tabulatoren keine Steuer- oder Formatzeichen enthalten.',
  en: 'The reason must be 1 to 1000 characters long without leading or trailing spaces, and may contain no control or format characters other than line breaks and tabs.',
};

const posts = () => requestsOf(fetch).filter(({ method }) => method === 'POST');

/** The page of a version in review with the return dialog open. */
async function openReturn() {
  const user = userEvent.setup();
  const rendered = await renderVersionPage(versionId, title);
  await user.click(screen.getByRole('button', { name: t('editorial.workflow.return') }));
  const dialog = await screen.findByRole('dialog', { name: t('editorial.workflow.returnForm.title') });
  const reason = within(dialog).getByRole('textbox', { name: t('editorial.workflow.returnForm.reason') });
  const confirm = within(dialog).getByRole('button', { name: t('editorial.workflow.returnForm.confirm') });
  return { user, dialog, reason, confirm, ...rendered };
}

describe('the return dialog', () => {
  test('asks for a reason, with the hint not to name people shown and in the field’s description', async () => {
    const { dialog, reason } = await openReturn();

    expect(dialog).toHaveAccessibleName('Version zurückgeben');
    expect(dialog).toHaveAccessibleDescription(
      'Die Version wird wieder ein Entwurf. Beschreiben Sie, was überarbeitet werden muss.',
    );
    expect(reason).toHaveAccessibleName('Grund');
    expect(reason.tagName).toBe('TEXTAREA');
    expect(reason).toHaveAttribute('aria-required', 'true');
    expect(reason).not.toHaveAttribute('aria-invalid', 'true');
    expect(within(dialog).getByText(hint)).toBeVisible();
    expect(reason).toHaveAccessibleDescription(hint);
    expect(within(dialog).getByRole('button', { name: 'Zurückgeben' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: t('editorial.cancel') })).toBeEnabled();
  });

  test('opens with the focus in the reason', async () => {
    const { reason } = await openReturn();

    await waitFor(() => expect(reason).toHaveFocus());
  });

  test('speaks English', async () => {
    setLanguage('en-GB', { remember: false });
    const { dialog, reason } = await openReturn();

    expect(dialog).toHaveAccessibleName('Return the version to draft');
    expect(dialog).toHaveAccessibleDescription(
      'The version becomes a draft again. Describe what needs to be reworked.',
    );
    expect(reason).toHaveAccessibleName('Reason');
    const english = 'Please do not name people in the reason; describe what needs to change in the version.';
    expect(within(dialog).getByText(english)).toBeVisible();
    expect(reason).toHaveAccessibleDescription(english);
    expect(within(dialog).getByRole('button', { name: 'Return to draft' })).toBeEnabled();
  });

  test('without a reason says one is needed, next to the field, and sends nothing', async () => {
    const { user, dialog, reason, confirm } = await openReturn();

    await user.type(reason, '   ');
    await user.click(confirm);

    const message = await within(dialog).findByRole('alert');
    expect(message).toHaveTextContent('Geben Sie einen Grund an.');
    expect(reason).toHaveAttribute('aria-invalid', 'true');
    expect(reason).toHaveAccessibleDescription(expect.stringContaining(hint));
    expect(reason).toHaveAccessibleDescription(expect.stringContaining('Geben Sie einen Grund an.'));
    expect(posts()).toEqual([]);
    expect(screen.getByRole('dialog')).toBeVisible();
  });

  test('without a reason says so in English', async () => {
    setLanguage('en-GB', { remember: false });
    const { user, dialog, confirm } = await openReturn();

    await user.click(confirm);

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Please give a reason.');
    expect(posts()).toEqual([]);
  });

  test('with a control character says it is not allowed and sends nothing', async () => {
    const { user, dialog, reason, confirm } = await openReturn();

    await user.click(reason);
    await user.paste('Bitte \u0007 überarbeiten');
    await user.click(confirm);

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Der Grund enthält unzulässige Steuer- oder Formatzeichen.',
    );
    expect(posts()).toEqual([]);
  });

  test('cancelled, sends nothing', async () => {
    const { user, dialog, reason } = await openReturn();
    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');

    await user.click(within(dialog).getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(posts()).toEqual([]);
  });

  test('posts the reason trimmed, reports the version a draft again and puts the focus on the heading', async () => {
    const { user, reason, confirm, heading } = await openReturn();

    await user.type(reason, '  Kapitel 2 fehlt die Quelle.{Enter}Frage 3 ist doppelt.  ');
    await user.click(confirm);

    await waitFor(() => expect(heading).toHaveFocus());
    expect(posts()).toEqual([
      {
        method: 'POST',
        path: `/editorial/course-versions/${versionId}/return`,
        body: { reason: 'Kapitel 2 fehlt die Quelle.\nFrage 3 ist doppelt.' },
      },
    ]);
    expect(notify.success).toHaveBeenCalledWith('Version 2 ist wieder ein Entwurf.');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByText(t('glossary.courseVersionStatus.Draft'))).toBeVisible();
    expect(screen.getByRole('button', { name: t('editorial.workflow.submit') })).toBeEnabled();
  });

  test('reports the return in English', async () => {
    setLanguage('en-GB', { remember: false });
    const { user, reason, confirm } = await openReturn();

    await user.type(reason, 'Chapter 2 lacks its source.');
    await user.click(confirm);

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Version 2 is a draft again.'));
  });

  test('disables its confirmation while the host has not answered', async () => {
    host.step = () => new Promise<Response>(() => undefined);
    const { user, dialog, reason, confirm } = await openReturn();

    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);

    await waitFor(() =>
      expect(within(dialog).getByRole('button', { name: t('editorial.workflow.returnForm.confirm') })).toBeDisabled(),
    );
    expect(posts()).toHaveLength(1);
  });

  // The host refuses a reason for more than a control character (ADR 0013), and the page cannot tell which rule it
  // broke: the field states the host's whole rule (#201), not only the client's character check.
  test('refused for its reason stays open with the host’s whole rule, the reason kept and the focus in it', async () => {
    host.step = async () => refused(400, 'editorial.invalid-return-reason');
    const { user, dialog, reason, confirm } = await openReturn();

    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);

    const message = await within(dialog).findByRole('alert');
    expect(message).toHaveTextContent(reasonRule.de);
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(reason).toHaveValue('Kapitel 2 fehlt die Quelle.');
    expect(reason).toHaveAttribute('aria-invalid', 'true');
    expect(reason).toHaveAccessibleDescription(`${hint} ${reasonRule.de}`);
    await waitFor(() => expect(reason).toHaveFocus());
    expect(notify.success).not.toHaveBeenCalled();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('refused for its reason states the host’s whole rule in English', async () => {
    setLanguage('en-GB', { remember: false });
    host.step = async () => refused(400, 'editorial.invalid-return-reason');
    const { user, dialog, reason, confirm } = await openReturn();

    await user.type(reason, 'Chapter 2 lacks its source.');
    await user.click(confirm);

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(reasonRule.en);
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(reason).toHaveValue('Chapter 2 lacks its source.');
    await waitFor(() => expect(reason).toHaveFocus());
  });

  test('refused because the version changed meanwhile stays open with the reason, says so, and returns it when sent again', async () => {
    host.step = async () => refused(409, 'editorial.course-version-changed');
    const { user, dialog, reason, confirm, heading } = await openReturn();

    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Die Version wurde inzwischen geändert. Sie sehen jetzt den aktuellen Stand; versuchen Sie es erneut.',
      ),
    );
    await waitFor(() => expect(reason).toHaveFocus());
    expect(screen.getByRole('dialog')).toBe(dialog);
    expect(reason).toHaveValue('Kapitel 2 fehlt die Quelle.');
    expect(reason).not.toHaveAttribute('aria-invalid');
    expect(confirm).toBeEnabled();

    host.step = async (step) => stepTaken(host, step);
    await user.click(confirm);

    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(posts().map(({ body }) => body)).toEqual([
      { reason: 'Kapitel 2 fehlt die Quelle.' },
      { reason: 'Kapitel 2 fehlt die Quelle.' },
    ]);
  });

  test('refused because the version is no longer in review closes, says so and puts the focus on the heading', async () => {
    host.step = async () => {
      host.version = versionOf('Published');
      return refused(409, 'editorial.not-in-review');
    };
    const { user, reason, confirm, heading } = await openReturn();

    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith('Die Version ist nicht mehr in Prüfung.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(heading).toHaveFocus());
    expect(notify.success).not.toHaveBeenCalled();
  });

  test('failing for another reason closes, says the version could not be returned and keeps the focus on Return', async () => {
    host.step = async () => new Response('', { status: 500 });
    const { user, reason, confirm } = await openReturn();

    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'Die Version konnte nicht zurückgegeben werden. Versuchen Sie es erneut.',
      ),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(screen.getByRole('button', { name: t('editorial.workflow.return') })).toHaveFocus());
  });

  test('with more than 1000 characters says how many it may have, next to the field, and sends nothing', async () => {
    const { user, dialog, reason, confirm } = await openReturn();

    await user.click(reason);
    await user.paste('a'.repeat(1001));
    await user.click(confirm);

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Der Grund darf höchstens 1000 Zeichen lang sein.',
    );
    expect(reason).toHaveAccessibleDescription(expect.stringContaining('höchstens 1000 Zeichen'));
    expect(posts()).toEqual([]);
  });

  // The host counts UTF-16 code units: an emoji outside the BMP is two.
  test('posts 500 emoji, 1000 UTF-16 code units as the host counts them', async () => {
    const { user, reason, confirm, heading } = await openReturn();

    await user.click(reason);
    await user.paste('\u{1F600}'.repeat(500));
    await user.click(confirm);

    await waitFor(() => expect(heading).toHaveFocus());
    expect(posts()).toEqual([
      {
        method: 'POST',
        path: `/editorial/course-versions/${versionId}/return`,
        body: { reason: '\u{1F600}'.repeat(500) },
      },
    ]);
  });

  test('refuses 1001 emoji, 1001 characters, as too long', async () => {
    const { user, dialog, reason, confirm } = await openReturn();

    await user.click(reason);
    await user.paste('\u{1F600}'.repeat(1001));
    await user.click(confirm);

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Der Grund darf höchstens 1000 Zeichen lang sein.',
    );
    expect(posts()).toEqual([]);
  });

  test.each(dialogClosings)('closes by %s while nothing is on its way, and sends nothing', async (way) => {
    const { dialog } = await openReturn();

    await tryToClose(dialog, way);

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(posts()).toEqual([]);
  });

  test.each(dialogClosings)('stays open by %s while the host has not answered, and posts once', async (way) => {
    host.step = () => new Promise<Response>(() => undefined);
    const { user, dialog, reason, confirm } = await openReturn();
    await user.type(reason, 'Kapitel 2 fehlt die Quelle.');
    await user.click(confirm);
    await waitFor(() => expect(confirm).toBeDisabled());

    await tryToClose(dialog, way);

    expect(screen.getByRole('dialog', { name: t('editorial.workflow.returnForm.title') })).toBeVisible();
    expect(reason).toHaveValue('Kapitel 2 fehlt die Quelle.');
    expect(posts()).toHaveLength(1);
  });
});

test('the return dialog is a file of its own', () => {
  expect(Object.keys(import.meta.glob('./ReturnVersionDialog.vue'))).toEqual(['./ReturnVersionDialog.vue']);
});
