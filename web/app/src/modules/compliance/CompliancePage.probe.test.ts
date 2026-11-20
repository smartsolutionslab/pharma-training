import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import CompliancePage from './CompliancePage.vue';
import type { ManagePermission } from './useCanManage';

// The page's samples come through useComplianceSamples, which the page's own test reads; here they are still loading.
vi.mock('./useComplianceSamples', () => ({
  useComplianceSamples: () => ({ samples: undefined, isLoading: true, isError: false }),
  useCreateComplianceSample: () => ({ create: () => Promise.resolve(false), isSaving: false }),
}));

// The page asks the module's permission probe, whose own test, useCanManage.test.ts, reads a 200, a 403 and a failure;
// here the answer is the test's. The page's axe check asks nothing of this, so a project that rewrites the page owns
// this test with it.
const probe = vi.hoisted(() => ({ permission: 'granted' as ManagePermission }));
vi.mock('./useCanManage', () => ({ useCanManage: () => probe.permission }));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

test('the Compliance page offers Manage when the probe answers 200', () => {
  probe.permission = 'granted';
  render(CompliancePage);

  expect(screen.getByRole('heading', { name: 'Manage' })).toBeInTheDocument();
  expect(screen.queryByText('Permissions for compliance could not be loaded.')).toBeNull();
});

test('the Compliance page offers no Manage when the probe refuses', () => {
  probe.permission = 'refused';
  render(CompliancePage);

  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
  expect(screen.queryByText('Permissions for compliance could not be loaded.')).toBeNull();
});

test('the Compliance page shows that its probe failed, not a refusal', () => {
  probe.permission = 'failed';
  render(CompliancePage);

  expect(screen.getByText('Permissions for compliance could not be loaded.')).toHaveAttribute('role', 'alert');
  expect(screen.queryByRole('heading', { name: 'Manage' })).toBeNull();
});
