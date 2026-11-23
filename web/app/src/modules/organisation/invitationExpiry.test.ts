import { expect, test } from 'vitest';
import { isExpired } from './invitationExpiry';

// An invitation is expired once its expiry lies before now (#12). The domain's comparison is strict
// (Pharmacy.IssueInvitationLink and AcceptInvitation: expiresAt < now), so at the very instant it expires the
// invitation is still open: the page must not call one expired that the host still accepts.

const expiresAt = '2026-11-22T12:00:00Z';

test('an invitation is open before it expires', () => {
  expect(isExpired(expiresAt, new Date('2026-11-22T11:59:59.999Z'))).toBe(false);
});

test('an invitation is still open at the very instant it expires', () => {
  expect(isExpired(expiresAt, new Date('2026-11-22T12:00:00Z'))).toBe(false);
});

test('an invitation is expired once its expiry has passed', () => {
  expect(isExpired(expiresAt, new Date('2026-11-22T12:00:00.001Z'))).toBe(true);
});

test('an invitation that expired days ago is expired, one that expires in days is open', () => {
  const now = new Date('2026-11-22T12:00:00Z');

  expect(isExpired('2026-11-21T12:00:00Z', now)).toBe(true);
  expect(isExpired('2026-12-01T12:00:00Z', now)).toBe(false);
});

// The host writes date-times with an offset or in UTC; both name the same instant.
test('an expiry with an offset is compared as the instant it names', () => {
  const now = new Date('2026-11-22T12:00:00Z');

  expect(isExpired('2026-11-22T13:00:00+01:00', now)).toBe(false);
  expect(isExpired('2026-11-22T12:59:59+01:00', now)).toBe(true);
});
