import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import CertificationPage from './CertificationPage.vue';
import type { ManagePermission } from './useCanManage';

// The page's samples come through useCertificationSamples, which the page's own test reads; here they are still loading.
vi.mock('./useCertificationSamples', () => ({
  useCertificationSamples: () => ({ samples: undefined, isLoading: true, isError: false }),
  useCreateCertificationSample: () => ({ create: () => Promise.resolve(false), isSaving: false }),
}));

// The page asks the module's permission probe, whose own test, useCanManage.test.ts, reads a 200, a 403 and a failure;
// here the answer is the test's. The page's axe check asks nothing of this, so a project that rewrites the page owns
// this test with it.
const probe = vi.hoisted(() => ({ permission: 'granted' as ManagePermission }));
vi.mock('./useCanManage', () => ({ useCanManage: () => probe.permission }));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

test('the Certification page offers Manage when the probe answers 200', () => {
  probe.permission = 'granted';
  render(CertificationPage);

  expect(screen.getByRole('heading', { name: 'Manage' })).toBeInTheDocument();
  expect(screen.queryByText('Permissions for certification could not be loaded.')).toBeNull();
});

test('the Certification page offers no Manage when the probe refuses', () => {
  probe.permission = 'refused';
  render(CertificationPage);

  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
  expect(screen.queryByText('Permissions for certification could not be loaded.')).toBeNull();
});

test('the Certification page shows that its probe failed, not a refusal', () => {
  probe.permission = 'failed';
  render(CertificationPage);

  expect(screen.getByText('Permissions for certification could not be loaded.')).toHaveAttribute('role', 'alert');
  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
});
