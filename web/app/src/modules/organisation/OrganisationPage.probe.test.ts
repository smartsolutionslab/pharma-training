import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import OrganisationPage from './OrganisationPage.vue';
import type { ManagePermission } from './useCanManage';

// The page's samples come through useOrganisationSamples, which the page's own test reads; here they are still loading.
vi.mock('./useOrganisationSamples', () => ({
  useOrganisationSamples: () => ({ samples: undefined, isLoading: true, isError: false }),
  useCreateOrganisationSample: () => ({ create: () => Promise.resolve(false), isSaving: false }),
}));

// The page asks the module's permission probe, whose own test, useCanManage.test.ts, reads a 200, a 403 and a failure;
// here the answer is the test's. The page's axe check asks nothing of this, so a project that rewrites the page owns
// this test with it.
const probe = vi.hoisted(() => ({ permission: 'granted' as ManagePermission }));
vi.mock('./useCanManage', () => ({ useCanManage: () => probe.permission }));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

test('the Organisation page offers Manage when the probe answers 200', () => {
  probe.permission = 'granted';
  render(OrganisationPage);

  expect(screen.getByRole('heading', { name: 'Manage' })).toBeInTheDocument();
  expect(screen.queryByText('Permissions for organisation could not be loaded.')).toBeNull();
});

test('the Organisation page offers no Manage when the probe refuses', () => {
  probe.permission = 'refused';
  render(OrganisationPage);

  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
  expect(screen.queryByText('Permissions for organisation could not be loaded.')).toBeNull();
});

test('the Organisation page shows that its probe failed, not a refusal', () => {
  probe.permission = 'failed';
  render(OrganisationPage);

  expect(screen.getByText('Permissions for organisation could not be loaded.')).toHaveAttribute('role', 'alert');
  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
});
