import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import LearningPage from './LearningPage.vue';
import { route } from './routes';
import type { ManagePermission } from './useCanManage';

// The page's samples come through useLearningSamples, which the page's own test reads; here they are still loading.
vi.mock('./useLearningSamples', () => ({
  useLearningSamples: () => ({ samples: undefined, isLoading: true, isError: false }),
  useCreateLearningSample: () => ({ create: () => Promise.resolve(false), isSaving: false }),
}));

// The page asks the module's permission probe, whose own test, useCanManage.test.ts, reads a 200, a 403 and a failure;
// here the answer is the test's. The page's axe check asks nothing of this, so a project that rewrites the page owns
// this test with it.
const probe = vi.hoisted(() => ({ permission: 'granted' as ManagePermission }));
vi.mock('./useCanManage', () => ({ useCanManage: () => probe.permission }));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The failed probe's alert, naming the module by its title in the language of the moment. */
const probeFailed = () => t('samples.permissionsFailed', { module: t(route.titleKey) });

test('the Learning page offers Manage when the probe answers 200', () => {
  probe.permission = 'granted';
  render(LearningPage);

  expect(screen.getByRole('heading', { name: t('samples.manage') })).toBeInTheDocument();
  expect(screen.queryByText(probeFailed())).toBeNull();
});

test('the Learning page offers no Manage when the probe refuses', () => {
  probe.permission = 'refused';
  render(LearningPage);

  expect(screen.queryByRole('heading', { name: t('samples.manage') })).toBeNull();
  expect(screen.queryByText(probeFailed())).toBeNull();
});

test('the Learning page shows that its probe failed, not a refusal', () => {
  probe.permission = 'failed';
  render(LearningPage);

  expect(screen.getByText(probeFailed())).toHaveAttribute('role', 'alert');
  expect(screen.queryByRole('heading', { name: t('samples.manage') })).toBeNull();
});

test('the Learning page names Manage and a failed probe in English once switched', async () => {
  probe.permission = 'granted';
  setLanguage('en-GB', { remember: false });
  render(LearningPage);

  expect(screen.getByRole('heading', { name: t('samples.manage') })).toBeInTheDocument();

  probe.permission = 'failed';
  cleanup();
  render(LearningPage);

  expect(await screen.findByText(probeFailed())).toHaveAttribute('role', 'alert');
});
