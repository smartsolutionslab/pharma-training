import { expect, test } from 'vitest';
import { statusLooks } from '@/components/status/assignmentStatus';

// A Locked cell counts as compliant (#276): it waits like Open, so it does not wear the colour of what needs action.
test('a locked cell does not wear the colour of an overdue or expired one', () => {
  expect(statusLooks.Locked.tone).not.toMatch(/destructive/);
  expect(statusLooks.Locked.needsAction).toBe(false);
  expect(statusLooks.Overdue.tone).toMatch(/destructive/);
  expect(statusLooks.Expired.tone).toMatch(/destructive/);
});
