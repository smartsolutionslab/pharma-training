import { cleanup, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { languages, setLanguage, t } from '@/i18n';
import { versionId, versionOf } from './courseTestFixtures';
import { answering, type Fetch, renderVersionPage, workflowHost } from './workflowPageHost';

// The return dialog's axe check (#26), in each language: open as it first shows, and with the reason's message shown.
// The dialog is portalled to the body, so axe checks the body.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

beforeEach(() => {
  fetch.mockImplementation(answering(workflowHost(versionOf('InReview'))));
});
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

async function openReturn() {
  const user = userEvent.setup();
  await renderVersionPage(versionId, 'Hygiene in der Apotheke');
  await user.click(screen.getByRole('button', { name: t('editorial.workflow.return') }));
  const dialog = await screen.findByRole('dialog', { name: t('editorial.workflow.returnForm.title') });
  return { user, dialog };
}

describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the open return dialog has no accessibility violations axe can find', async () => {
    await openReturn();

    expect(await axe(document.body)).toHaveNoViolations();
  });

  test('the return dialog showing the reason’s message has no accessibility violations axe can find', async () => {
    const { user, dialog } = await openReturn();
    await user.click(within(dialog).getByRole('button', { name: t('editorial.workflow.returnForm.confirm') }));
    await within(dialog).findByRole('alert');

    expect(await axe(document.body)).toHaveNoViolations();
  });
});
