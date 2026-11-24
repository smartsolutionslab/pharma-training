import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  answering,
  type Fetch,
  fixClock,
  invitationsPath,
  inviteButton,
  never,
  problem,
  readsOf,
  renderTeamPage,
  rowOf,
  type TeamHost,
  teamHost,
  type User,
  writesOf,
} from './teamFixtures';

// Inviting a member (#12, ADR 0004): a dialog asks for the address, the job function and the role, checks them as
// the host does (the address trimmed, required, at most 254 characters and of an address's shape; a job function and
// a role of the glossary) and posts { email, jobFunction, role }. A refusal keeps the dialog open with what was
// entered, says why inside it and in a toast, and reads the invitations again.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: TeamHost;

beforeEach(() => {
  localStorage.clear();
  fixClock();
  host = teamHost();
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  fetch.mockReset();
  vi.useRealTimers();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const posts = () => writesOf(fetch).filter(({ method }) => method === 'POST');
const invitationReads = () => readsOf(fetch, invitationsPath).length;

/** The page with the invite dialog open, from the header's Invite button. */
async function openInvite() {
  const user = userEvent.setup();
  await renderTeamPage();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  const element = await screen.findByRole('dialog', { name: t('organisation.team.invite.title') });
  const dialog = within(element);
  return {
    user,
    element,
    dialog,
    email: dialog.getByRole('textbox', { name: t('organisation.team.invite.email') }),
    jobFunction: dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') }),
    role: dialog.getByRole('combobox', { name: t('organisation.team.invite.role') }),
    submit: dialog.getByRole('button', { name: t('organisation.team.invite.submit') }),
  };
}

/** Fills in the address (pasted, so a long one is quick) and picks a job function by its label. */
async function fillIn(user: User, fields: { email: HTMLElement; jobFunction: HTMLElement }, email: string) {
  await user.click(fields.email);
  await user.paste(email);
  await user.selectOptions(fields.jobFunction, t('glossary.jobFunction.Pta'));
}

test('the dialog says what an invitation is and asks for the address, the job function and the role', async () => {
  const { element, dialog, email, jobFunction, role, submit } = await openInvite();

  expect(element).toHaveAccessibleDescription(t('organisation.team.invite.description'));
  expect(email).toHaveAttribute('type', 'email');
  expect(email).toHaveAttribute('autocomplete', 'off');
  expect(email).toHaveValue('');
  expect(jobFunction).toHaveValue('');
  expect(
    within(jobFunction)
      .getAllByRole('option')
      .map((option) => [option.textContent.trim(), option.getAttribute('value')]),
  ).toEqual([
    [t('organisation.team.invite.choose'), ''],
    [t('glossary.jobFunction.Pharmacist'), 'Pharmacist'],
    [t('glossary.jobFunction.Pta'), 'Pta'],
    [t('glossary.jobFunction.Pka'), 'Pka'],
    [t('glossary.jobFunction.Trainee'), 'Trainee'],
    [t('glossary.jobFunction.Other'), 'Other'],
  ]);
  expect(role).toHaveValue('Employee');
  expect(
    within(role)
      .getAllByRole('option')
      .map((option) => option.getAttribute('value')),
  ).toEqual(expect.arrayContaining(['Manager', 'Employee']));
  expect(within(role).getByRole('option', { name: t('glossary.membershipRole.Manager') })).toHaveValue('Manager');
  expect(submit).toBeEnabled();
  expect(dialog.getByRole('button', { name: t('organisation.team.cancel') })).toBeEnabled();
});

test('opens with the focus in the address', async () => {
  const { email } = await openInvite();

  await waitFor(() => expect(email).toHaveFocus());
});

test('an empty form is refused with a message beside each field, and nothing is sent', async () => {
  const { user, dialog, email, jobFunction, submit } = await openInvite();

  await user.click(submit);

  const alerts = await dialog.findAllByRole('alert');
  expect(alerts.map((alert) => alert.textContent.trim())).toEqual([
    t('organisation.team.invite.emailRequired'),
    t('organisation.team.invite.jobFunctionRequired'),
  ]);
  expect(email).toHaveAttribute('aria-invalid', 'true');
  expect(email).toHaveAccessibleDescription(t('organisation.team.invite.emailRequired'));
  expect(jobFunction).toHaveAttribute('aria-invalid', 'true');
  expect(jobFunction).toHaveAccessibleDescription(t('organisation.team.invite.jobFunctionRequired'));
  expect(posts()).toEqual([]);
  expect(screen.getByRole('dialog')).toBeVisible();
});

test('an address of spaces only is no address', async () => {
  const fields = await openInvite();
  await fillIn(fields.user, fields, '   ');

  await fields.user.click(fields.submit);

  expect(await fields.dialog.findByRole('alert')).toHaveTextContent(t('organisation.team.invite.emailRequired'));
  expect(posts()).toEqual([]);
});

test('a@b is not an address', async () => {
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'a@b');

  await fields.user.click(fields.submit);

  expect(await fields.dialog.findByRole('alert')).toHaveTextContent(t('organisation.team.invite.emailInvalid'));
  expect(posts()).toEqual([]);
});

// 255 characters, of an address's shape: one more than an address may have.
test('an address of 255 characters is too long', async () => {
  const fields = await openInvite();
  const address = `${'a'.repeat(243)}@example.com`;
  expect(address).toHaveLength(255);
  await fillIn(fields.user, fields, address);

  await fields.user.click(fields.submit);

  expect(await fields.dialog.findByRole('alert')).toHaveTextContent(t('organisation.team.invite.emailTooLong'));
  expect(posts()).toEqual([]);
});

test('posts the trimmed address, the job function and the role, closes, reports it and lists the invitation', async () => {
  const fields = await openInvite();
  const before = invitationReads();
  await fillIn(fields.user, fields, '  dora@example.com  ');

  await fields.user.click(fields.submit);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.invite.invited', { email: 'dora@example.com' })),
  );
  expect(posts()).toEqual([
    {
      method: 'POST',
      path: invitationsPath,
      body: { email: 'dora@example.com', jobFunction: 'Pta', role: 'Employee' },
    },
  ]);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
  const row = await rowOf('dora@example.com');
  expect(row.getByText(t('organisation.team.invitations.open'))).toBeInTheDocument();
  expect(notify.failure).not.toHaveBeenCalled();
});

test('posts the role chosen', async () => {
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');
  await fields.user.selectOptions(fields.role, t('glossary.membershipRole.Manager'));

  await fields.user.click(fields.submit);

  await waitFor(() =>
    expect(posts()).toEqual([
      {
        method: 'POST',
        path: invitationsPath,
        body: { email: 'dora@example.com', jobFunction: 'Pta', role: 'Manager' },
      },
    ]),
  );
});

test('opened again after an invitation, the dialog is empty', async () => {
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');
  await fields.user.click(fields.submit);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

  await fields.user.click(inviteButton());

  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.invite.title') }));
  expect(dialog.getByRole('textbox', { name: t('organisation.team.invite.email') })).toHaveValue('');
  expect(dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') })).toHaveValue('');
  expect(dialog.getByRole('combobox', { name: t('organisation.team.invite.role') })).toHaveValue('Employee');
  expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
});

test('a double click sends one invitation, and the button is disabled while the host has not answered', async () => {
  host.invite = never;
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');

  await fields.user.dblClick(fields.submit);

  await waitFor(() => expect(fields.submit).toBeDisabled());
  await fields.user.click(fields.submit);
  expect(posts()).toHaveLength(1);
});

// The host's own check (already-member-or-invited) and the unique index that lost a race (already-exists) report one
// fact, so the page says the same for both.
test.each([
  ['the handler’s 409 already-member-or-invited', () => problem(409, 'organisation.already-member-or-invited', 'x')],
  ["the unique index's 409 already-exists", () => problem(409, 'already-exists', 'Conflict')],
])(
  '%s keeps the dialog open with what was entered, says why in it and in a toast, and reads the list again',
  async (_answer, answer) => {
    host.invite = async () => answer();
    const fields = await openInvite();
    const before = invitationReads();
    await fillIn(fields.user, fields, 'ben@example.com');

    await fields.user.click(fields.submit);

    const message = t('errors.organisation.already-member-or-invited');
    expect(await fields.dialog.findByRole('alert')).toHaveTextContent(message);
    expect(notify.failure).toHaveBeenCalledWith(message);
    expect(notify.success).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(fields.email).toHaveValue('ben@example.com');
    expect(fields.jobFunction).toHaveValue('Pta');
    expect(fields.role).toHaveValue('Employee');
    expect(fields.submit).toBeEnabled();
    await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
  },
);

test('an address the host refuses becomes the address field’s message', async () => {
  host.invite = async () => problem(400, 'organisation.invalid-email', 'The email is invalid.');
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');

  await fields.user.click(fields.submit);

  const message = t('errors.organisation.invalid-email');
  await waitFor(() => expect(fields.email).toHaveAccessibleDescription(expect.stringContaining(message)));
  expect(fields.email).toHaveAttribute('aria-invalid', 'true');
  expect(notify.failure).toHaveBeenCalledWith(message);
  expect(screen.getByRole('dialog')).toBeVisible();
});

test('failing for another reason keeps the dialog open and says the invitation could not be sent', async () => {
  host.invite = async () => new Response('', { status: 500 });
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');

  await fields.user.click(fields.submit);

  const message = t('organisation.team.invite.failed', { email: 'dora@example.com' });
  expect(await fields.dialog.findByRole('alert')).toHaveTextContent(message);
  expect(notify.failure).toHaveBeenCalledWith(message);
  expect(fields.email).toHaveValue('dora@example.com');
});

test('cancelled, sends nothing', async () => {
  const fields = await openInvite();
  await fillIn(fields.user, fields, 'dora@example.com');

  await fields.user.click(fields.dialog.getByRole('button', { name: t('organisation.team.cancel') }));

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(posts()).toEqual([]);
});

// The empty list's action opened the dialog; once the first invitation is listed, that action is gone, and the focus
// goes back to the header's Invite button.
test('after the first invitation from the empty list the focus goes back to Invite', async () => {
  const user = userEvent.setup();
  host.invitations = [];
  await renderTeamPage();
  await user.click(await screen.findByRole('button', { name: t('organisation.team.invitations.inviteFirst') }));
  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.invite.title') }));
  await fillIn(
    user,
    {
      email: dialog.getByRole('textbox', { name: t('organisation.team.invite.email') }),
      jobFunction: dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') }),
    },
    'dora@example.com',
  );

  await user.click(dialog.getByRole('button', { name: t('organisation.team.invite.submit') }));

  await rowOf('dora@example.com');
  await waitFor(() => expect(inviteButton()).toHaveFocus());
});

test('asks and refuses in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  const { user, element, dialog, submit } = await openInvite();

  expect(element).toHaveAccessibleName('Invite a member');
  await user.click(submit);

  const alerts = await dialog.findAllByRole('alert');
  expect(alerts.map((alert) => alert.textContent.trim())).toEqual([
    'Enter the email address.',
    'Choose the job function.',
  ]);
});
