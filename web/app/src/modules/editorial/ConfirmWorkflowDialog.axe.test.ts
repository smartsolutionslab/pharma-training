import { cleanup, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import type { EditorialCourseVersionStatus } from '@/api/generated';
import { languages, setLanguage, t } from '@/i18n';
import { versionId, versionOf } from './courseTestFixtures';
import { answering, type Fetch, renderVersionPage, workflowHost } from './workflowPageHost';

// The confirmation dialogs' axe check (#26), in each language: publishing a version in review and retiring a
// published one. The dialog is portalled to the body, so axe checks the body.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const dialogs: [string, EditorialCourseVersionStatus, string, string][] = [
  ['publish', 'InReview', 'editorial.workflow.publish', 'editorial.workflow.publishConfirm.title'],
  ['retire', 'Published', 'editorial.workflow.retire', 'editorial.workflow.retireConfirm.title'],
];

describe.each(languages)('in %s', (language) => {
  test.each(dialogs)(
    'the dialog confirming to %s has no accessibility violations axe can find',
    async (_step, status, action, dialogTitle) => {
      setLanguage(language, { remember: false });
      fetch.mockImplementation(answering(workflowHost(versionOf(status))));
      const user = userEvent.setup();
      await renderVersionPage(versionId, 'Hygiene in der Apotheke');

      await user.click(screen.getByRole('button', { name: t(action) }));
      await screen.findByRole('dialog', { name: t(dialogTitle) });

      expect(await axe(document.body)).toHaveNoViolations();
    },
  );
});
