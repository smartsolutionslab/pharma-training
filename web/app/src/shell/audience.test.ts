import { expect, test } from 'vitest';
import type { MyPharmacyDto } from '@/api/generated';
import { canReach, moduleOf } from './audience';

// Memberships as GET /organisation/pharmacies/mine answers them: rights come from the capabilities, not the role's name.
const membership = (overrides: Partial<MyPharmacyDto>): MyPharmacyDto => ({
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
  ...overrides,
});

const employee = membership({});
const manager = membership({ role: 'Manager', canManageMembers: true, canAssignCourses: true });
const membersOnly = membership({ role: 'Manager', canManageMembers: true });
const coursesOnly = membership({ role: 'Manager', canAssignCourses: true });
const managerInNameOnly = membership({ role: 'Manager' });

test.each([
  // employee: any pharmacy chosen
  ['employee', undefined, false, false],
  ['employee', undefined, true, false],
  ['employee', employee, false, true],
  ['employee', manager, false, true],
  // manager: the chosen membership may manage members or assign courses
  ['manager', undefined, false, false],
  ['manager', employee, false, false],
  ['manager', managerInNameOnly, false, false],
  ['manager', membersOnly, false, true],
  ['manager', coursesOnly, false, true],
  ['manager', manager, false, true],
  ['manager', employee, true, false],
  // editor: the realm role editor, whatever the pharmacy
  ['editor', undefined, true, true],
  ['editor', manager, true, true],
  ['editor', undefined, false, false],
  ['editor', manager, false, false],
] as const)(
  'the %s audience with membership %o and editor %s is reachable: %s',
  (audience, current, isEditor, reachable) => {
    expect(canReach(audience, { current, isEditor })).toBe(reachable);
  },
);

test("moduleOf finds the module an address is in by the address's first segment", () => {
  expect(moduleOf('/organisation')?.path).toBe('organisation');
  expect(moduleOf('/organisation/members/0198c1a0-0000-7000-8000-0000000000b1')?.path).toBe('organisation');
  expect(moduleOf('/learning/')?.path).toBe('learning');
});

test('moduleOf finds no module for the start page or an address no module has', () => {
  expect(moduleOf('/')).toBeUndefined();
  expect(moduleOf('/nowhere')).toBeUndefined();
  expect(moduleOf('/organisations')).toBeUndefined();
});
