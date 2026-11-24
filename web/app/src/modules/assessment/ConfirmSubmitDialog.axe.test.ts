import { cleanup, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { languages, setLanguage, t } from '@/i18n';
import { chosen, coat, durationQuestion, inAttempt, protectionQuestion, thirtySeconds } from './examinationFixtures';
import { type Fetch, hostAnswering, renderAt, routed, testAddress } from './examinationHost';
import ExaminationPage from './ExaminationPage.vue';

// The submit confirmation's axe check (#48), in each language: with an unanswered question (and its warning), and
// with every question answered. The dialog is portalled to the body, so axe checks the body.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);
afterEach(() => fetch.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const attempts = [
  ['one question unanswered', inAttempt([chosen(durationQuestion, [thirtySeconds]), protectionQuestion])],
  [
    'every question answered',
    inAttempt([chosen(durationQuestion, [thirtySeconds]), chosen(protectionQuestion, [coat])]),
  ],
] as const;

describe.each(languages)('in %s', (language) => {
  test.each(attempts)(
    'the dialog confirming the submit with %s has no violations axe can find',
    async (_case, read) => {
      setLanguage(language, { remember: false });
      fetch.mockImplementation(routed(hostAnswering(read)));
      const user = userEvent.setup();
      await renderAt(ExaminationPage, testAddress);

      await user.click(await screen.findByRole('button', { name: t('assessment.test.submit') }));
      await screen.findByRole('dialog', { name: t('assessment.test.confirm.title') });

      expect(await axe(document.body)).toHaveNoViolations();
    },
  );
});
