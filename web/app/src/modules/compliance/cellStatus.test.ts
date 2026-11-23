import { expect, test } from 'vitest';
import { cellStatus } from './cellStatus';

// What a cell of the team matrix (#36) shows: the assignment's state as the host sends it, or Expired, which the host
// does not name. A completed cell (Completed or CompletedLate) is expired once its completion no longer holds: its
// validUntil is the last valid day, inclusive, so it expires at local midnight after that day. The host's rate already
// counts such a cell as not compliant; the page says why.

const states = ['Open', 'InProgress', 'Locked', 'Overdue', 'Completed', 'CompletedLate'] as const;

/** 2026-11-23 at 10:00 local time. */
const today = new Date(2026, 10, 23, 10, 0, 0);

test.each(states)('a cell %s with no validUntil is %s', (state) => {
  expect(cellStatus({ state, validUntil: null }, today)).toBe(state);
});

test.each(states)('a cell %s still valid is %s', (state) => {
  expect(cellStatus({ state, validUntil: '2027-11-23' }, today)).toBe(state);
});

// Only a completion expires: a state that is not one keeps its own word, whatever date the cell carries.
test.each(['Open', 'InProgress', 'Locked', 'Overdue'] as const)(
  'a cell %s with a validUntil in the past stays %s',
  (state) => {
    expect(cellStatus({ state, validUntil: '2026-11-01' }, today)).toBe(state);
  },
);

test.each(['Completed', 'CompletedLate'] as const)('a cell %s valid until 2026-11-23 holds all that day', (state) => {
  const cell = { state, validUntil: '2026-11-23' };

  expect(cellStatus(cell, new Date(2026, 10, 22, 23, 59, 59))).toBe(state);
  expect(cellStatus(cell, new Date(2026, 10, 23, 0, 0, 0))).toBe(state);
  expect(cellStatus(cell, new Date(2026, 10, 23, 23, 59, 0))).toBe(state);
  expect(cellStatus(cell, new Date(2026, 10, 23, 23, 59, 59, 999))).toBe(state);
});

test.each(['Completed', 'CompletedLate'] as const)(
  'a cell %s valid until 2026-11-23 is Expired from 2026-11-24 at local midnight',
  (state) => {
    const cell = { state, validUntil: '2026-11-23' };

    expect(cellStatus(cell, new Date(2026, 10, 24, 0, 0, 0))).toBe('Expired');
    expect(cellStatus(cell, new Date(2026, 10, 24, 10, 0, 0))).toBe('Expired');
    expect(cellStatus(cell, new Date(2027, 0, 1))).toBe('Expired');
  },
);

test.each(['Completed', 'CompletedLate'] as const)('a cell %s without validUntil never expires', (state) => {
  expect(cellStatus({ state, validUntil: null }, new Date(2126, 0, 1))).toBe(state);
});
