import { expect, test } from 'vitest';
import type { MyPharmacyDto } from '@/api/generated';
import { canReach, moduleOf, pageOf } from './audience';

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
  // signed-in: every signed-in user, with or without a pharmacy or a role
  ['signed-in', undefined, false, true],
  ['signed-in', undefined, true, true],
  ['signed-in', employee, false, true],
  ['signed-in', manager, true, true],
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

// A module's own page below it (its routes.ts's pages) governs its address; any other address in the module is the module's.
test("pageOf finds the module's page by the address's second segment", () => {
  expect(pageOf('/organisation/register')).toMatchObject({
    path: 'register',
    titleKey: 'modules.organisation.pages.register.title',
    audience: 'signed-in',
  });
});

test('pageOf finds the module for an address in it that is none of its pages', () => {
  expect(pageOf('/organisation')).toMatchObject({
    path: 'organisation',
    titleKey: 'modules.organisation.title',
    audience: 'manager',
  });
  expect(pageOf('/organisation/members')).toMatchObject({ path: 'organisation', audience: 'manager' });
  expect(pageOf('/learning/register')).toMatchObject({ path: 'learning', audience: 'employee' });
});

test('pageOf finds nothing for the start page or an address no module has', () => {
  expect(pageOf('/')).toBeUndefined();
  expect(pageOf('/nowhere')).toBeUndefined();
  expect(pageOf('/nowhere/register')).toBeUndefined();
});

// A detail page below a module (its routes.ts's details) has segments that may hold a parameter, `versions/:id`: a
// static segment is equal, a parameter matches one segment that is not empty. A detail governs its address before the
// module's pages and the module.
const version = '0198c1a0-0000-7000-8000-0000000000c1';

test("pageOf finds a module's detail page by its pattern, a parameter matching one segment and no more", () => {
  expect(pageOf(`/editorial/versions/${version}`)).toMatchObject({
    path: 'versions/:id',
    titleKey: 'modules.editorial.details.version.title',
    audience: 'editor',
  });
  expect(pageOf(`/editorial/versions/${version}/`)).toMatchObject({ path: 'versions/:id', audience: 'editor' });

  // An address in the module that matches no detail's pattern is the module's.
  expect(pageOf('/editorial/versions')).toMatchObject({ path: 'editorial', titleKey: 'modules.editorial.title' });
  expect(pageOf(`/editorial/versions/${version}/chapters`)).toMatchObject({ path: 'editorial' });
  expect(pageOf(`/editorial/courses/${version}`)).toMatchObject({ path: 'editorial' });
});

// An entry page (a module's routes.ts's entries) is at an address of its own, outside its module's: no module owns the
// segment invitations. It governs its exact address by its own audience, before any module's.
test('pageOf finds an entry page by its exact address, governed by its own audience', () => {
  expect(pageOf('/invitations/accept')).toMatchObject({
    path: '/invitations/accept',
    titleKey: 'modules.organisation.entries.acceptInvitation.title',
    audience: 'signed-in',
  });
});
